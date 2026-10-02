using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class UsersModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["members.list", "users.list", "users.activate", "users.deactivate", "users.setRole"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        if (actor is null) throw ApiException.Forbidden();
        if (r.Action == "members.list")
        {
            actor.Active();
            return await ModuleQueries.Page(db,r,"api_members");
        }
        actor.Admin();
        if (r.Action == "users.list")
        {
            var role = r.Optional("role", 30);
            if (role is not null && !new[] { "SUPER_ADMIN","REGISTRADOR","SOLDADO_PENDING","SOLDADO_ACTIVE","SOLDADO_INACTIVE" }.Contains(role)) throw ApiException.Invalid("Rol inválido.");
            return await ModuleQueries.Page(db, r, "api_users", role is null ? "TRUE" : "role=@role", role is null ? [] : [("role", role)]);
        }
        var id = r.Id();
        var user = await db.One("SELECT json_build_object('role',role)::text FROM users WHERE id=@id FOR UPDATE", ("id", id)) ?? throw ApiException.Missing();
        var oldRole = user.GetProperty("role").GetString();
        if (id == actor.Id) throw ApiException.Invalid("No puedes cambiar tu propia cuenta.");
        if (r.Action == "users.setRole")
        {
            if (actor.Role != "SUPER_ADMIN") throw ApiException.Forbidden();
            var role = r.Choice("role", "REGISTRADOR", "REGISTRADOR", "SOLDADO_ACTIVE");
            if (oldRole == "SUPER_ADMIN") throw ApiException.Forbidden();
            await db.Execute("UPDATE users SET role=@role WHERE id=@id", ("id", id), ("role", role));
        }
        else
        {
            if (oldRole is "SUPER_ADMIN" or "REGISTRADOR") throw ApiException.Forbidden();
            var active = r.Action == "users.activate";
            await db.Execute("UPDATE users SET role=@role WHERE id=@id", ("id", id), ("role", active ? "SOLDADO_ACTIVE" : "SOLDADO_INACTIVE"));
            await db.Execute("UPDATE certificate_reviews SET status=@status,review_note=@note,reviewed_by_user_id=@actor,reviewed_at=now() WHERE user_id=@id AND status='PENDING'",
                ("status", active ? "APPROVED" : "REJECTED"), ("note", r.Optional("reviewNote", 2000)), ("actor", actor.Id), ("id", id));
        }
        // El rol se lee en cada petición; la desactivación revoca también los tokens existentes.
        if (r.Action == "users.deactivate") await db.Execute("DELETE FROM sessions WHERE user_id=@id", ("id", id));
        await ModuleQueries.Audit(db, actor, r.Action, id);
        return await ModuleQueries.Get(db, "api_users", id);
    }
}
