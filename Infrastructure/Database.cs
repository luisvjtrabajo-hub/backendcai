using System.Text.Json;
using Npgsql;

namespace Cai.Api.Infrastructure;

// Una conexión y transacción por solicitud. Los módulos reciben SQL parametrizado.
public interface IDatabase
{
    Task<int> Execute(string sql, params (string Name, object? Value)[] args);
    Task<JsonElement?> One(string sql, params (string Name, object? Value)[] args);
    Task<IReadOnlyList<JsonElement>> Many(string sql, params (string Name, object? Value)[] args);
    Task<long> Count(string sql, params (string Name, object? Value)[] args);
}

public sealed class Database(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct) : IDatabase
{
    private NpgsqlCommand Command(string sql, (string Name, object? Value)[] args)
    {
        var cmd = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
    public async Task<int> Execute(string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = Command(sql, args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<JsonElement?> One(string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = Command(sql, args);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : JsonSerializer.Deserialize<JsonElement>((string)value);
    }
    public async Task<IReadOnlyList<JsonElement>> Many(string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = Command(sql, args);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var items = new List<JsonElement>();
        while (await reader.ReadAsync(ct)) items.Add(JsonSerializer.Deserialize<JsonElement>(reader.GetString(0)));
        return items;
    }
    public async Task<long> Count(string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = Command(sql, args);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }
}

public static class ConnectionSettings
{
    public static string Parse(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            // Render usa contraseña y SSL, sin Kerberos/GSSAPI en la imagen .NET.
            var settings = new NpgsqlConnectionStringBuilder(value) { GssEncryptionMode = GssEncryptionMode.Disable };
            return settings.ConnectionString;
        }
        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);
        if (credentials.Length != 2) throw new InvalidOperationException("DATABASE_URL debe incluir usuario y contraseña.");
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1]),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            SslMode = SslMode.Require, GssEncryptionMode = GssEncryptionMode.Disable,
            MaxPoolSize = 20, Timeout = 15, CommandTimeout = 30
        };
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "sslmode")
                builder.SslMode = pair[1].ToLowerInvariant() switch
                {
                    "disable" => SslMode.Disable, "require" => SslMode.Require,
                    "verify-full" => SslMode.VerifyFull, "verify-ca" => SslMode.VerifyCA,
                    _ => throw new InvalidOperationException("sslmode no soportado; use disable, require o verify-full.")
                };
        }
        return builder.ConnectionString;
    }
}
