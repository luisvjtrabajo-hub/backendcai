using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

internal static class ModuleQueries
{
    // view/where solo proceden de constantes internas, nunca de valores del cliente.
    public static async Task<object> Page(IDatabase db, ApiRequest r, string view, string where = "TRUE", params (string Name, object? Value)[] args)
    {
        var page = r.Number("page", 1, 1, 100000);
        var size = r.Number("pageSize", 50, 1, 100);
        var total = await db.Count($"SELECT count(*) FROM {view} WHERE {where}", args);
        var items = await db.Many($"SELECT row_to_json(t)::text FROM (SELECT * FROM {view} WHERE {where} ORDER BY \"createdAt\" DESC,id LIMIT @size OFFSET @offset) t", [..args, ("size", size), ("offset", (page - 1) * size)]);
        return new { items, total, page, pageSize = size };
    }
    public static async Task<object> Get(IDatabase db, string view, Guid id) =>
        await db.One($"SELECT row_to_json(t)::text FROM {view} t WHERE id=@id", ("id", id)) ?? throw ApiException.Missing();
    public static Task<int> Audit(IDatabase db, Actor actor, string action, Guid target) => db.Execute(
        "INSERT INTO audit_log(actor_user_id,action,target_id) VALUES(@actor,@action,@target)", ("actor", actor.Id), ("action", action), ("target", target));
}
