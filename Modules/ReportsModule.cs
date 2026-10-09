using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class ReportsModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["sectRegistry.list","sectReports.list","sectReports.create","sectReports.review","overview.get"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        var user = actor ?? throw ApiException.Forbidden();
        user.Active();
        if (r.Action == "sectRegistry.list") return await ModuleQueries.PageProjected(db,r,"api_sect_reports","status='APPROVED'","v.*,(SELECT latitude FROM sect_reports WHERE id=v.id) AS latitude,(SELECT longitude FROM sect_reports WHERE id=v.id) AS longitude");
        if (r.Action == "sectReports.list")
        {
            var status = r.Choice("status","PENDING","PENDING","APPROVED","REJECTED");
            return await ModuleQueries.PageProjected(db,r,"api_sect_reports", user.IsAdmin ? "status=@status" : "status=@status AND \"reportedByUserId\"=@user",
                "v.*,(SELECT latitude FROM sect_reports WHERE id=v.id) AS latitude,(SELECT longitude FROM sect_reports WHERE id=v.id) AS longitude",
                user.IsAdmin ? [("status",status)] : [("status",status),("user",user.Id)]);
        }
        if (r.Action == "sectReports.create")
        {
            if(!user.IsAdmin)
            {
                var profile=await RankSystem.Profile(db,user.Id,true);
                if(profile.GetProperty("reserve").GetBoolean() || profile.GetProperty("level").GetInt32()<2) throw ApiException.Forbidden();
                if(profile.GetProperty("birthDate").ValueKind==System.Text.Json.JsonValueKind.Null) throw ApiException.Invalid("Completa tu fecha de nacimiento antes de registrar una ficha.");
                if(DateOnly.Parse(profile.GetProperty("birthDate").GetString()!)>DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18)) throw ApiException.Forbidden();
            }
            var id = Guid.NewGuid();
            await db.Execute("INSERT INTO sect_reports(id,sect_name,location_description,reference_note,reported_by_user_id) VALUES(@id,@name,@location,@note,@user)",
                ("id",id),("name",r.Required("sectName",180)),("location",r.Required("locationDescription",1000)),("note",r.Required("referenceNote",4000)),("user",user.Id));
            var latitude=r.Coordinate("latitude",-85.0511,85.0511); var longitude=r.Coordinate("longitude",-180,180);
            if((latitude is null)!=(longitude is null)) throw ApiException.Invalid("Indica latitud y longitud juntas.");
            await db.Execute("UPDATE sect_reports SET latitude=@lat,longitude=@lon WHERE id=@id",("id",id),("lat",latitude),("lon",longitude));
            return await ModuleQueries.Get(db,"api_sect_reports",id);
        }
        user.Admin();
        if (r.Action == "overview.get")
        {
            return new {
                users = new { activeCount = await db.Count("SELECT count(*) FROM users WHERE role='SOLDADO_ACTIVE'"), pendingCount = await db.Count("SELECT count(*) FROM users WHERE role='SOLDADO_PENDING'") },
                missions = new { publishedCount = await db.Count("SELECT count(*) FROM missions WHERE publication_state='PUBLISHED'") },
                missionSubmissions = new { pendingCount = await db.Count("SELECT count(*) FROM mission_submissions WHERE status='PENDING'") },
                sectReports = new { pendingCount = await db.Count("SELECT count(*) FROM sect_reports WHERE status='PENDING'") },
                certificateReviews = new { pendingCount = await db.Count("SELECT count(*) FROM certificate_reviews WHERE status='PENDING'") }
            };
        }
        var target = r.Id();
        var statusToSet = r.Choice("status","APPROVED","APPROVED","REJECTED");
        if (await db.Execute("UPDATE sect_reports SET status=@status,review_note=@note,reviewed_by_user_id=@actor,reviewed_at=now(),approved_at=CASE WHEN @status='APPROVED' THEN now() ELSE NULL END WHERE id=@id AND status='PENDING'",
            ("status",statusToSet),("note",r.Optional("reviewNote",2000)),("actor",user.Id),("id",target)) == 0)
            throw new ApiException(409,"ALREADY_REVIEWED","El reporte no existe o ya fue revisado.");
        await ModuleQueries.Audit(db,user,r.Action,target);
        return await ModuleQueries.Get(db,"api_sect_reports",target);
    }
}
