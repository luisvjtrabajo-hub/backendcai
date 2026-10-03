using System.Text.Json;

namespace Cai.Api.Api;

public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public static ApiException Invalid(string message) => new(400, "VALIDATION_ERROR", message);
    public static ApiException Missing() => new(404, "NOT_FOUND", "No se encontró el registro.");
    public static ApiException Forbidden() => new(403, "FORBIDDEN", "No tienes permisos para esta operación.");
}

public sealed record Actor(Guid Id, string Role)
{
    public bool IsAdmin => Role is "SUPER_ADMIN" or "REGISTRADOR";
    public void Admin() { if (!IsAdmin) throw ApiException.Forbidden(); }
    public void Active() { if (!IsAdmin && Role != "SOLDADO_ACTIVE") throw ApiException.Forbidden(); }
}

public sealed record ApiRequest(string Action, JsonElement Data, IFormFile? File)
{
    public string? Optional(string name, int max = 4000)
    {
        if (Data.ValueKind != JsonValueKind.Object || !Data.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw ApiException.Invalid($"{name} debe ser texto.");
        var text = value.GetString()!.Trim();
        if (text.Length > max) throw ApiException.Invalid($"{name}: máximo {max} caracteres.");
        return text.Length == 0 ? null : text;
    }
    public string Required(string name, int max = 4000) => Optional(name, max) ?? throw ApiException.Invalid($"{name} es obligatorio.");
    public string Password()
    {
        if (!Data.TryGetProperty("password", out var p) || p.ValueKind != JsonValueKind.String || p.GetString() is not { Length: > 0 and <= 128 } password)
            throw ApiException.Invalid("password es obligatorio y admite hasta 128 caracteres.");
        return password;
    }
    public Guid Id(string name = "id") => Guid.TryParse(Required(name, 36), out var id) && id != Guid.Empty ? id : throw ApiException.Invalid($"{name} debe ser UUID.");
    public int Number(string name, int fallback, int min, int max)
    {
        if (!Data.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
            throw ApiException.Invalid($"{name} debe estar entre {min} y {max}.");
        return number;
    }
    public bool Flag(string name)
    {
        if (!Data.TryGetProperty(name,out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw ApiException.Invalid($"{name} debe ser booleano.");
        return value.GetBoolean();
    }
    public string Choice(string name, string fallback, params string[] choices)
    {
        var value = Optional(name, 100) ?? fallback;
        return choices.Contains(value) ? value : throw ApiException.Invalid($"{name}: valores válidos {string.Join(", ", choices)}.");
    }
    public static async Task<ApiRequest> Read(HttpRequest request, CancellationToken ct)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count > 1) throw ApiException.Invalid("Solo se admite un archivo por operación.");
            return Create(form["action"].ToString(), form["data"].ToString() is { Length: > 0 } data ? JsonSerializer.Deserialize<JsonElement>(data) : JsonSerializer.SerializeToElement(new { }), form.Files.GetFile("file"));
        }
        if (!request.HasJsonContentType()) throw new ApiException(415, "UNSUPPORTED_MEDIA_TYPE", "Usa application/json o multipart/form-data.");
        var json = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body, cancellationToken: ct);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("action", out var a) || a.ValueKind != JsonValueKind.String)
            throw ApiException.Invalid("action es obligatorio.");
        return Create(a.GetString()!, json.TryGetProperty("data", out var d) ? d : JsonSerializer.SerializeToElement(new { }), null);
    }
    private static ApiRequest Create(string action, JsonElement data, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(action) || action.Length > 100 || data.ValueKind != JsonValueKind.Object)
            throw ApiException.Invalid("Envía action y data como objeto JSON.");
        return new ApiRequest(action, data, file);
    }
}
