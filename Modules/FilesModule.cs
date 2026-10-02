using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class FilesModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["files.get"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        return await db.One("""
            SELECT json_build_object('id',id,'name',name,'contentType',content_type,
            'base64',replace(encode(content,'base64'),E'\n',''))::text
            FROM files WHERE id=@id AND (owner_user_id=@user OR @admin)
            """, ("id", r.Id()), ("user", actor!.Id), ("admin", actor.IsAdmin)) ?? throw ApiException.Missing();
    }
    public static async Task<Guid> Store(ApiRequest r, Actor actor, IDatabase db, CancellationToken ct)
    {
        var file = r.File ?? throw ApiException.Invalid("Adjunta un archivo en el campo file.");
        if (file.Length is <= 0 or > 5 * 1024 * 1024) throw ApiException.Invalid("El archivo debe pesar entre 1 byte y 5 MB.");
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        var content = memory.ToArray();
        var type = Detect(content);
        var id = Guid.NewGuid();
        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw ApiException.Invalid("Nombre de archivo inválido.");
        await db.Execute("INSERT INTO files(id,owner_user_id,name,content_type,content) VALUES(@id,@owner,@name,@type,@content)",
            ("id", id), ("owner", actor.Id), ("name", name), ("type", type), ("content", content));
        return id;
    }
    private static string Detect(byte[] b)
    {
        if (b.AsSpan().StartsWith(new byte[] { 0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a })) return "image/png";
        if (b.AsSpan().StartsWith(new byte[] { 0xff,0xd8,0xff })) return "image/jpeg";
        if (b.AsSpan().StartsWith("%PDF-"u8)) return "application/pdf";
        if (b.AsSpan().StartsWith("GIF87a"u8) || b.AsSpan().StartsWith("GIF89a"u8)) return "image/gif";
        if (b.Length >= 12 && b.AsSpan(0,4).SequenceEqual("RIFF"u8) && b.AsSpan(8,4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw ApiException.Invalid("Formatos permitidos: JPG, PNG, WebP, GIF y PDF. El contenido debe coincidir con el formato.");
    }
}
