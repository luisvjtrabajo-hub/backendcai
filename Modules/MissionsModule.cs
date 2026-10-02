using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class MissionsModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["missions.list", "missions.create", "missions.update", "missions.publish", "missions.archive", "submissions.create", "submissions.list", "submissions.review", "progress.get", "history.get"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        var user = actor ?? throw ApiException.Forbidden();
        user.Active();
        if (r.Action == "missions.list")
            return await ModuleQueries.Page(db, r, "api_missions", user.IsAdmin ? "TRUE" : "\"publicationState\"='PUBLISHED' AND CASE \"minimumRankCode\" WHEN 'RECRUTA' THEN 0 WHEN 'SOLDADO' THEN 1 WHEN 'CABO' THEN 2 ELSE 3 END <= (SELECT CASE rank_code WHEN 'RECRUTA' THEN 0 WHEN 'SOLDADO' THEN 1 WHEN 'CABO' THEN 2 ELSE 3 END FROM users WHERE id=@user)", user.IsAdmin ? [] : [("user",user.Id)]);
        if (r.Action == "submissions.list")
        {
            var status = r.Optional("status",20);
            if (status is not null && !new[] { "PENDING","APPROVED","REJECTED" }.Contains(status)) throw ApiException.Invalid("Estado inválido.");
            var where = user.IsAdmin ? "TRUE" : "\"userId\"=@user";
            var args = new List<(string,object?)>();
            if (!user.IsAdmin) args.Add(("user",user.Id));
            if (status is not null) { where += " AND status=@status"; args.Add(("status",status)); }
            return await ModuleQueries.Page(db,r,"api_submissions",where,[..args]);
        }
        if (r.Action is "progress.get" or "history.get")
        {
            var progress = await db.One("""
                SELECT json_build_object('rankCode',u.rank_code,'totalBadgeWeight',coalesce((SELECT sum(m.badge_weight) FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=u.id AND s.status='APPROVED'),0),'completedMissionTotal',(SELECT count(*) FROM mission_submissions s WHERE s.user_id=u.id AND s.status='APPROVED'))::text FROM users u WHERE u.id=@user
                """,("user",user.Id));
            if (r.Action == "progress.get") return progress!.Value;
            return new { completedMissionTotal = progress!.Value.GetProperty("completedMissionTotal").GetInt64(), history = await ModuleQueries.Page(db,r,"api_submissions","\"userId\"=@user",("user",user.Id)) };
        }
        if (r.Action == "submissions.create")
        {
            var missionId = r.Id("missionId");
            // Publicación/rango comprobados en servidor, no solo en las pantallas.
            var mission = await db.One("SELECT json_build_object('rank',minimum_rank_code)::text FROM missions WHERE id=@id AND publication_state='PUBLISHED' FOR SHARE",("id",missionId)) ?? throw ApiException.Missing();
            var rank = await db.One("SELECT json_build_object('rank',rank_code)::text FROM users WHERE id=@id",("id",user.Id));
            var ranks = new[] { "RECRUTA","SOLDADO","CABO","SARGENTO" };
            if (!user.IsAdmin && Array.IndexOf(ranks,rank!.Value.GetProperty("rank").GetString()) < Array.IndexOf(ranks,mission.GetProperty("rank").GetString())) throw ApiException.Forbidden();
            var existing = await db.One("SELECT json_build_object('id',id,'status',status)::text FROM mission_submissions WHERE mission_id=@mission AND user_id=@user FOR UPDATE", ("mission",missionId),("user",user.Id));
            if (existing is not null && existing.Value.GetProperty("status").GetString() != "REJECTED")
                throw new ApiException(409,"SUBMISSION_EXISTS","Ya tienes una evidencia pendiente o aprobada en esta misión.");
            var fileId = await FilesModule.Store(r,user,db,ct);
            var id = existing?.GetProperty("id").GetGuid() ?? Guid.NewGuid();
            if (existing is null)
                await db.Execute("INSERT INTO mission_submissions(id,mission_id,user_id,file_id,submission_note) VALUES(@id,@mission,@user,@file,@note)",("id",id),("mission",missionId),("user",user.Id),("file",fileId),("note",r.Optional("submissionNote",2000)));
            else
            {
                await db.Execute("UPDATE mission_submissions SET file_id=@file,submission_note=@note,status='PENDING',review_note=NULL,reviewed_at=NULL,reviewed_by_user_id=NULL WHERE id=@id",("id",id),("file",fileId),("note",r.Optional("submissionNote",2000)));
            }
            return await ModuleQueries.Get(db,"api_submissions",id);
        }
        user.Admin();
        if (r.Action == "submissions.review")
        {
            var id = r.Id();
            var record = await db.One("SELECT json_build_object('userId',user_id)::text FROM mission_submissions WHERE id=@id",("id",id)) ?? throw ApiException.Missing();
            var target = record.GetProperty("userId").GetGuid();
            // Serializa aprobaciones para calcular el rango sin perder incrementos concurrentes.
            await db.Execute("SELECT id FROM users WHERE id=@id FOR UPDATE",("id",target));
            var status = r.Choice("status","APPROVED","APPROVED","REJECTED");
            if (await db.Execute("UPDATE mission_submissions SET status=@status,review_note=@note,reviewed_by_user_id=@actor,reviewed_at=now() WHERE id=@id AND status='PENDING'",("status",status),("note",r.Optional("reviewNote",2000)),("actor",user.Id),("id",id)) == 0)
                throw new ApiException(409,"ALREADY_REVIEWED","La evidencia ya fue revisada.");
            await db.Execute("""
                UPDATE users SET rank_code=CASE WHEN p.weight>=20 THEN 'SARGENTO' WHEN p.weight>=10 THEN 'CABO' WHEN p.weight>=3 THEN 'SOLDADO' ELSE 'RECRUTA' END
                FROM (SELECT coalesce(sum(m.badge_weight),0) AS weight FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=@user AND s.status='APPROVED') p WHERE id=@user
                """,("user",target));
            await ModuleQueries.Audit(db,user,r.Action,id);
            return await ModuleQueries.Get(db,"api_submissions",id);
        }
        if (r.Action is "missions.create" or "missions.update")
        {
            var id = r.Action == "missions.create" ? Guid.NewGuid() : r.Id();
            var args = new (string,object?)[] { ("id",id),("title",r.Required("title",180)),("description",r.Required("description",4000)),
                ("type",r.Choice("missionType","OPERACIONAL","OPERACIONAL","FORMATIVA","ESPIRITUAL")),
                ("rank",r.Choice("minimumRankCode","RECRUTA","RECRUTA","SOLDADO","CABO","SARGENTO")),
                ("gender",r.Choice("genderEligibility","ALL","ALL")),("weight",r.Number("badgeWeight",1,1,100)) };
            if (r.Action == "missions.create")
                await db.Execute("INSERT INTO missions(id,title,description,mission_type,minimum_rank_code,gender_eligibility,badge_weight,created_by_user_id) VALUES(@id,@title,@description,@type,@rank,@gender,@weight,@actor)",[..args,("actor",user.Id)]);
            else if (await db.Execute("UPDATE missions SET title=@title,description=@description,mission_type=@type,minimum_rank_code=@rank,gender_eligibility=@gender,badge_weight=@weight WHERE id=@id AND publication_state='DRAFT'",args) == 0)
                throw new ApiException(409,"INVALID_STATE","Solo se pueden editar borradores existentes.");
            await ModuleQueries.Audit(db,user,r.Action,id);
            return await ModuleQueries.Get(db,"api_missions",id);
        }
        var missionToChange = r.Id();
        var changed = r.Action == "missions.publish"
            ? await db.Execute("UPDATE missions SET publication_state='PUBLISHED',published_at=now() WHERE id=@id AND publication_state='DRAFT'",("id",missionToChange))
            : await db.Execute("UPDATE missions SET publication_state='ARCHIVED' WHERE id=@id AND publication_state IN ('DRAFT','PUBLISHED')",("id",missionToChange));
        if (changed == 0) throw new ApiException(409,"INVALID_STATE","La misión no existe o no admite esta transición.");
        await ModuleQueries.Audit(db,user,r.Action,missionToChange);
        return await ModuleQueries.Get(db,"api_missions",missionToChange);
    }
}
