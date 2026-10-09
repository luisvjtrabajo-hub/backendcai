using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Cai.Api.Api;
using Cai.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace Cai.Api.Modules;

public sealed class AuthModule(IConfiguration configuration, CertificatesModule certificates) : IActionHandler, IDisposable
{
    private readonly PasswordHasher<string> hasher = new();
    private readonly string dummyHash = new PasswordHasher<string>().HashPassword("", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    private readonly PartitionedRateLimiter<string> attempts = PartitionedRateLimiter.Create<string, string>(email =>
        RateLimitPartition.GetFixedWindowLimiter(email, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(15), QueueLimit = 0, AutoReplenishment = true }));
    public IReadOnlyCollection<string> Actions { get; } = ["auth.register", "auth.login", "auth.me", "auth.logout"];
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string Email(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(value, out var parsed) || parsed.Address != value || value.Length > 254)
            throw ApiException.Invalid("Correo electrónico inválido.");
        return value;
    }
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        if (r.Action == "auth.me") return await Profile(db, actor!.Id);
        if (r.Action == "auth.logout")
        {
            // Cierra todas las sesiones del usuario; útil también si pierde un dispositivo.
            await db.Execute("DELETE FROM sessions WHERE user_id=@id", ("id", actor!.Id));
            return new { message = "Sesiones cerradas." };
        }
        var email = Email(r.Required("email", 254));
        var password = r.Password();
        using var lease = await attempts.AcquireAsync(email, 1, ct);
        if (!lease.IsAcquired) throw new ApiException(429, "RATE_LIMITED", "Demasiados intentos. Intenta nuevamente en 15 minutos.");
        Guid id;
        if (r.Action == "auth.register")
        {
            if (password.Length < 8) throw ApiException.Invalid("La contraseña debe tener al menos 8 caracteres.");
            r.Id("locationId");
            id = Guid.NewGuid();
            await db.Execute("INSERT INTO users(id,email,full_name,password_hash) VALUES(@id,@email,@name,@hash)",
                ("id", id), ("email", email), ("name", r.Required("fullName", 160)), ("hash", hasher.HashPassword(email, password)));
            await LocationsModule.Save(r, db, id);
            var activation = r.Choice("activationMode", "NONE", "NONE", "NUMBER", "REVIEW");
            if (activation != "NONE")
            {
                // Registro y certificado atómicos: un fallo permite corregir y reintentar.
                await certificates.Handle(r with { Action = activation == "NUMBER" ? "activation.claim" : "activation.submit" },
                    new Actor(id,"SOLDADO_PENDING"),db,ct);
            }
            else if (r.File is not null) throw ApiException.Invalid("Indica activationMode=REVIEW para adjuntar un certificado.");
        }
        else
        {
            var record = await db.One("SELECT json_build_object('id',id,'hash',password_hash,'role',role)::text FROM users WHERE email=@email", ("email", email));
            var hash = record?.GetProperty("hash").GetString() ?? dummyHash;
            var result = hasher.VerifyHashedPassword(email, hash, password);
            if (record is null || result == PasswordVerificationResult.Failed)
                throw new ApiException(401, "INVALID_CREDENTIALS", "Correo o contraseña incorrectos.");
            if (record.Value.GetProperty("role").GetString() == "SOLDADO_INACTIVE")
                throw new ApiException(403, "ACCOUNT_INACTIVE", "Tu cuenta está desactivada. Contacta con el administrador.");
            id = record.Value.GetProperty("id").GetGuid();
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                await db.Execute("UPDATE users SET password_hash=@hash WHERE id=@id", ("hash", hasher.HashPassword(email, password)), ("id", id));
        }
        var hours = configuration.GetValue("Auth:SessionHours", 24);
        if (hours is < 1 or > 168) throw new InvalidOperationException("Auth:SessionHours debe estar entre 1 y 168.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTimeOffset.UtcNow.AddHours(hours);
        await db.Execute("DELETE FROM sessions WHERE expires_at<now()");
        await db.Execute("INSERT INTO sessions(token_hash,user_id,expires_at) VALUES(@hash,@id,@expiry)",
            ("hash", TokenHash(token)), ("id", id), ("expiry", expiresAt));
        return new { accessToken = token, expiresAt, user = await Profile(db, id) };
    }
    private static async Task<object> Profile(IDatabase db, Guid id) =>
        (await db.One("SELECT row_to_json(t)::text FROM (SELECT v.*,u.country,u.city,u.location_id AS \"locationId\",l.country_code AS \"countryCode\" FROM api_users v JOIN users u ON u.id=v.id LEFT JOIN city_locations l ON l.id=u.location_id WHERE v.id=@id)t", ("id", id))) ?? throw ApiException.Missing();
    public void Dispose() => attempts.Dispose();
}
