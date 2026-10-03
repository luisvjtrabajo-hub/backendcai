using Cai.Api.Modules;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace Cai.Api.Infrastructure;

public static class Bootstrap
{
    public static async Task Initialize(NpgsqlDataSource source, IConfiguration config, string contentRoot, CancellationToken ct)
    {
        if (config.GetValue("Database:AutoMigrate", true))
        {
            await using var setup = source.CreateCommand("CREATE TABLE IF NOT EXISTS schema_migrations(name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())");
            await setup.ExecuteNonQueryAsync(ct);
            foreach (var file in Directory.GetFiles(Path.Combine(contentRoot,"database"),"*.sql").Order(StringComparer.Ordinal))
            {
                await using var check = source.CreateCommand("SELECT count(*) FROM schema_migrations WHERE name=@name");
                check.Parameters.AddWithValue("name",Path.GetFileName(file));
                if (Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0) continue;
                await using var cmd = source.CreateCommand(await File.ReadAllTextAsync(file,ct));
                await cmd.ExecuteNonQueryAsync(ct);
                await using var record = source.CreateCommand("INSERT INTO schema_migrations(name) VALUES(@name) ON CONFLICT DO NOTHING");
                record.Parameters.AddWithValue("name",Path.GetFileName(file));
                await record.ExecuteNonQueryAsync(ct);
            }
        }
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrEmpty(password)) return;
        if (string.IsNullOrWhiteSpace(email) || password is not { Length: >= 12 and <= 128 })
            throw new InvalidOperationException("Configura Bootstrap__AdminEmail y Bootstrap__AdminPassword (12–128 caracteres). No existe una contraseña predeterminada.");
        email = AuthModule.Email(email);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var db = new Database(connection,transaction,ct);
        await db.Execute("SELECT pg_advisory_xact_lock(824671231)");
        // Creación única: jamás sobrescribe la contraseña ni promueve cuentas existentes.
        await db.Execute("INSERT INTO users(email,full_name,password_hash,role) VALUES(@email,@name,@hash,'SUPER_ADMIN') ON CONFLICT(email) DO NOTHING",
            ("email",email),("name",config["Bootstrap:AdminName"] ?? "Administrador CAI"),("hash",new PasswordHasher<string>().HashPassword(email,password)));
        var user = await db.One("SELECT json_build_object('role',role)::text FROM users WHERE email=@email",("email",email));
        if (user!.Value.GetProperty("role").GetString() != "SUPER_ADMIN") throw new InvalidOperationException("El correo de bootstrap pertenece a una cuenta existente sin rol SUPER_ADMIN. Usa otro correo.");
        await transaction.CommitAsync(ct);
    }
}
