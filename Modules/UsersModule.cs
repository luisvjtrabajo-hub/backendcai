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
            var (where, args) = LocationsModule.Filters(r);
            var parameters = args.ToList();
            if(r.Optional("q",120) is {} text) {where+=" AND v.\"fullName\" ILIKE @q";parameters.Add(("q","%"+text+"%"));}
            if(r.Optional("rankCode",30) is {} rank) {where+=" AND v.\"rankCode\"=@rank";parameters.Add(("rank",rank));}
            return await ModuleQueries.PageProjected(db,r,"(SELECT m.*,u.country,u.city FROM api_members m JOIN users u ON u.id=m.id)",where,"v.*",[..parameters]);
        }
        actor.Admin();
        if (r.Action == "users.list")
        {
            var role = r.Optional("role", 30);
            if (role is not null && !new[] { "SUPER_ADMIN","REGISTRADOR","SOLDADO_PENDING","SOLDADO_ACTIVE","SOLDADO_INACTIVE" }.Contains(role)) throw ApiException.Invalid("Rol inválido.");
            var where=role is null ? "TRUE" : "v.role=@role";
            var args=new List<(string,object?)>(); if(role is not null) args.Add(("role",role));
            foreach(var (name,column) in new[]{("q","v.\"fullName\""),("city","(SELECT city FROM users WHERE id=v.id)"),("country","(SELECT country FROM users WHERE id=v.id)")})
                if(r.Optional(name,120) is {} text) {where+=$" AND {column} ILIKE @"+name; args.Add((name,"%"+text+"%"));}
            if(r.Optional("rankCode",30) is {} rank) {where+=" AND v.\"rankCode\"=@rank"; args.Add(("rank",rank));}
            if(r.Optional("area",80) is {} area) {where+=" AND EXISTS(SELECT 1 FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=v.id AND s.status='APPROVED' AND m.area=@area)";args.Add(("area",area));}
            if(r.Optional("activity",20) is not null) {var activity=r.Choice("activity","ACTIVE","ACTIVE","RESERVE","INACTIVE_60");where+=activity=="RESERVE" ? " AND EXISTS(SELECT 1 FROM users WHERE id=v.id AND reserve)" : activity=="INACTIVE_60" ? " AND EXISTS(SELECT 1 FROM activity_alerts WHERE user_id=v.id AND days_inactive>=60)" : " AND EXISTS(SELECT 1 FROM users WHERE id=v.id AND NOT reserve)";}
            return await ModuleQueries.PageProjected(db,r,"api_users",where,
                "v.*,(SELECT json_build_object('birthDate',u.birth_date,'parentalConsent',u.parental_consent,'reserve',u.reserve,'sponsorId',u.sponsor_id,'formationStartedAt',u.formation_started_at,'legacyRankCode',u.legacy_rank_code,'city',u.city,'country',u.country,'locationId',u.location_id,'countryCode',(SELECT country_code FROM city_locations WHERE id=u.location_id),'lastActivityAt',greatest(u.activity_resumed_at,coalesce((SELECT max(s.occurred_at) FROM mission_submissions s WHERE s.user_id=u.id AND s.status='APPROVED'),u.created_at)),'areas',coalesce((SELECT json_agg(t.area) FROM (SELECT DISTINCT m.area FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=u.id AND s.status='APPROVED')t),'[]'::json)) FROM users u WHERE u.id=v.id) AS profile",[..args]);
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
            if(active) await db.Execute("UPDATE users SET reserve=false,activity_resumed_at=now() WHERE id=@id",("id",id));
            await db.Execute("UPDATE certificate_reviews SET status=@status,review_note=@note,reviewed_by_user_id=@actor,reviewed_at=now() WHERE user_id=@id AND status='PENDING'",
                ("status", active ? "APPROVED" : "REJECTED"), ("note", r.Optional("reviewNote", 2000)), ("actor", actor.Id), ("id", id));
        }
        // El rol se lee en cada petición; la desactivación revoca también los tokens existentes.
        if (r.Action == "users.deactivate") await db.Execute("DELETE FROM sessions WHERE user_id=@id", ("id", id));
        await ModuleQueries.Audit(db, actor, r.Action, id);
        return await ModuleQueries.Get(db, "api_users", id);
    }
}
