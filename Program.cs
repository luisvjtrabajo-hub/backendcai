using System.Text.Json;
using System.Threading.RateLimiting;
using Cai.Api.Api;
using Cai.Api.Infrastructure;
using Cai.Api.Modules;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    if (!int.TryParse(port,out var number) || number is < 1 or > 65535) throw new InvalidOperationException("PORT inválido.");
    builder.WebHost.UseUrls($"http://0.0.0.0:{number}");
}
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = 6 * 1024 * 1024; o.ValueLengthLimit = 64 * 1024; });
var connection = builder.Configuration["DATABASE_URL"] ?? builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("Configura DATABASE_URL o ConnectionStrings__Database.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(ConnectionSettings.Parse(connection)));
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (origins.Length == 0 && builder.Environment.IsDevelopment()) origins = ["http://localhost:5173"];
if (origins.Length == 0 || origins.Any(o => !Uri.TryCreate(o,UriKind.Absolute,out var u) || u.AbsolutePath != "/" || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment) || o.EndsWith('/') || u.Scheme is not ("http" or "https")))
    throw new InvalidOperationException("Configura Cors__AllowedOrigins__0 con el origen exacto de Vercel, sin barra final.");
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).WithMethods("POST","GET").WithHeaders("Content-Type","Authorization")));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddConcurrencyLimiter("api",p => { p.PermitLimit = 32; p.QueueLimit = 0; });
    o.OnRejected = async (context,ct) => await context.HttpContext.Response.WriteAsJsonAsync(new { error="RATE_LIMITED", message="Servidor ocupado. Intenta nuevamente en unos segundos." },ct);
});
builder.Services.AddSingleton<IActionHandler,AuthModule>();
builder.Services.AddSingleton<IActionHandler,UsersModule>();
builder.Services.AddSingleton<CertificatesModule>();
builder.Services.AddSingleton<IActionHandler>(services => services.GetRequiredService<CertificatesModule>());
builder.Services.AddSingleton<IActionHandler,MissionsModule>();
builder.Services.AddSingleton<IActionHandler,ReportsModule>();
builder.Services.AddSingleton<IActionHandler,FilesModule>();
builder.Services.AddSingleton<Dispatcher>();
var app = builder.Build();
app.Use(async (context,next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try { await next(context); }
    catch (Exception exception) when (!context.Response.HasStarted)
    {
        var (status,code,message) = exception switch
        {
            ApiException e => (e.Status,e.Code,e.Message),
            JsonException => (400,"INVALID_JSON","JSON inválido."),
            InvalidDataException => (400,"INVALID_FORM","Formulario inválido o demasiado grande."),
            BadHttpRequestException e => (e.StatusCode,"INVALID_REQUEST","Solicitud inválida o demasiado grande."),
            PostgresException { SqlState: "23505" } => (409,"DUPLICATE","El registro ya existe o fue utilizado."),
            PostgresException { SqlState: "23503" } => (400,"INVALID_REFERENCE","El registro relacionado no existe."),
            PostgresException { SqlState: "23514" or "22001" } => (400,"VALIDATION_ERROR","Los datos no cumplen las restricciones."),
            PostgresException { SqlState: "40P01" or "40001" } => (409,"CONCURRENT_CHANGE","Otra operación modificó los datos. Intenta nuevamente."),
            NpgsqlException => (503,"DATABASE_UNAVAILABLE","La base de datos no está disponible. Intenta nuevamente."),
            _ => (500,"INTERNAL_ERROR","No se pudo completar la operación.")
        };
        if (status >= 500) app.Logger.LogError(exception,"Error de API. TraceId: {TraceId}",context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error=code,message,traceId=context.TraceIdentifier });
    }
});
app.UseCors();
app.UseRateLimiter();
app.MapGet("/healthz",async (NpgsqlDataSource db,CancellationToken ct) =>
{
    await using var cmd = db.CreateCommand("SELECT 1");
    await cmd.ExecuteScalarAsync(ct);
    return Results.Ok(new { status="ok" });
});
app.MapPost("/api",async (HttpContext context, NpgsqlDataSource source, Dispatcher dispatcher,CancellationToken ct) =>
{
    var request = await ApiRequest.Read(context.Request,ct);
    await using var connection = await source.OpenConnectionAsync(ct);
    await using var transaction = await connection.BeginTransactionAsync(ct);
    var result = await dispatcher.Execute(request,context.Request.Headers.Authorization,new Database(connection,transaction,ct),ct);
    await transaction.CommitAsync(ct);
    return Results.Json(result);
}).RequireRateLimiting("api");
await Bootstrap.Initialize(app.Services.GetRequiredService<NpgsqlDataSource>(),app.Configuration,app.Environment.ContentRootPath,app.Lifetime.ApplicationStopping);
await app.RunAsync();

public partial class Program;
