using System.Text.Json;
using System.Threading.RateLimiting;
using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

// Only provider results can supply coordinates. Clients select a stored location ID.
public sealed class LocationsModule(IHttpClientFactory clients, IConfiguration configuration) : IActionHandler, IDisposable
{
    private readonly FixedWindowRateLimiter searches = new(new FixedWindowRateLimiterOptions
    { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
    public IReadOnlyCollection<string> Actions { get; } = ["locations.search", "members.map"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        if (r.Action == "members.map")
        {
            if (actor is null) throw ApiException.Forbidden();
            actor.Admin();
            var (where, args) = Filters(r);
            return await ModuleQueries.Page(db, r, "api_member_locations", where, args);
        }
        var query = r.Required("q", 100);
        if (query.Length < 2) throw ApiException.Invalid("Escribe al menos dos letras de la ciudad.");
        var country = r.Required("countryCode", 2).ToUpperInvariant();
        if (country.Length != 2 || country.Any(c => c is < 'A' or > 'Z')) throw ApiException.Invalid("Selecciona un país válido.");
        using var lease = await searches.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new ApiException(429, "RATE_LIMITED", "Demasiadas búsquedas de ciudades. Intenta nuevamente en un minuto.");
        var key = configuration["Locations:ApiKey"];
        var path = $"search?name={Uri.EscapeDataString(query)}&countryCode={country}&language=es&count=10";
        if (!string.IsNullOrEmpty(key)) path += "&apikey=" + Uri.EscapeDataString(key);
        JsonDocument document;
        try
        {
            using var response = await clients.CreateClient("geocoding").GetAsync(path, ct);
            response.EnsureSuccessStatusCode();
            document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new ApiException(503, "LOCATION_SEARCH_UNAVAILABLE", "No se pudo buscar la ciudad. Intenta nuevamente."); }
        using (document)
        {
            var items = new List<JsonElement>();
            if (document.RootElement.TryGetProperty("results", out var results))
            foreach (var item in results.EnumerateArray())
            {
                // GeoNames populated places only; exclude mountains, postal regions, etc.
                if (!item.TryGetProperty("feature_code", out var feature) || !feature.GetString()!.StartsWith("PPL", StringComparison.Ordinal) ||
                    item.GetProperty("country_code").GetString() != country) continue;
                var name = item.GetProperty("name").GetString()!;
                var countryName = item.GetProperty("country").GetString()!;
                if (name.Length > 120 || countryName.Length > 120) continue;
                var region = item.TryGetProperty("admin1", out var regionValue) ? regionValue.GetString() : null;
                await db.Execute("INSERT INTO city_locations(id,provider_id,country,country_code,city,region,latitude,longitude) VALUES(@id,@provider,@country,@code,@city,@region,@lat,@lon) ON CONFLICT(provider_id) DO NOTHING",
                    ("id", Guid.NewGuid()), ("provider", item.GetProperty("id").GetInt32()), ("country", countryName),
                    ("code", country), ("city", name), ("region", region), ("lat", item.GetProperty("latitude").GetDouble()), ("lon", item.GetProperty("longitude").GetDouble()));
                items.Add((await db.One("SELECT json_build_object('id',id,'country',country,'countryCode',country_code,'city',city,'region',region)::text FROM city_locations WHERE provider_id=@provider", ("provider", item.GetProperty("id").GetInt32())))!.Value);
            }
            return new { items };
        }
    }
    internal static (string, (string, object?)[]) Filters(ApiRequest r)
    {
        var where = "TRUE"; var args = new List<(string, object?)>();
        foreach (var name in new[] { "country", "city" })
            if (r.Optional(name, 120) is { } text) { where += $" AND v.{name} ILIKE @{name}"; args.Add((name, "%" + text + "%")); }
        return (where, [.. args]);
    }
    internal static async Task Save(ApiRequest r, IDatabase db, Guid userId)
    {
        var location = r.Id("locationId");
        if (await db.Execute("UPDATE users u SET location_id=l.id,country=l.country,city=l.city FROM city_locations l WHERE u.id=@user AND l.id=@location", ("user", userId), ("location", location)) == 0)
            throw ApiException.Invalid("Busca y selecciona una ciudad válida.");
    }
    public void Dispose() => searches.Dispose();
}
