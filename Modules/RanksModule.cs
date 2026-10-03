using System.Text.Json;
using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class RanksModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions {get;}=["ranks.get","profile.update","milestones.validate","points.list","activity.list","conduct.create","conduct.list","conduct.review","evidence.upload"];
    public async Task<object> Handle(ApiRequest r,Actor? actor,IDatabase db,CancellationToken ct)
    {
        var user=actor ?? throw ApiException.Forbidden(); user.Active();
        if(r.Action=="ranks.get") return new {ranks=await db.Many("SELECT data::text FROM cai_ranks ORDER BY level"),milestones=await db.Many("SELECT (data || CASE WHEN code='HIT-GM' THEN jsonb_build_object('requirement','Elección del Capítulo General, trayectoria completa y obra de la Orden fundada y sostenida.') ELSE '{}'::jsonb END)::text FROM cai_milestones ORDER BY level")};
        if(r.Action=="points.list") return await ModuleQueries.Page(db,r,"api_points","\"userId\"=@id",("id",user.Id));
        if(r.Action=="evidence.upload")
        {
            var p=await RankSystem.Profile(db,user.Id);
            if(p.GetProperty("reserve").GetBoolean()) throw ApiException.Forbidden();
            return new {id=await FilesModule.Store(r,user,db,ct,"MISSION_EVIDENCE")};
        }
        if(r.Action=="profile.update")
        {
            var target=r.Optional("userId",36) is null ? user.Id : r.Id("userId");
            if(target!=user.Id) user.Admin();
            await RankSystem.Profile(db,target,true);
            var birth=r.Required("birthDate",10);
            if(!DateOnly.TryParseExact(birth,"yyyy-MM-dd",out var date) || date>DateOnly.FromDateTime(DateTime.UtcNow) || date<DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-120)) throw ApiException.Invalid("Fecha de nacimiento inválida.");
            if(!user.IsAdmin && await db.Count("SELECT count(*) FROM users WHERE id=@id AND birth_date IS NOT NULL AND birth_date<>@birth",("id",target),("birth",date))>0) throw ApiException.Invalid("Solo el administrador puede corregir una fecha ya registrada.");
            await db.Execute("UPDATE users SET birth_date=@birth WHERE id=@id",("id",target),("birth",date));
            if(user.IsAdmin)
            {
                r.Required("reviewNote",2000);
                Guid? sponsor=r.Optional("sponsorId",36) is null ? null : r.Id("sponsorId");
                if(sponsor==target || (sponsor is not null && await db.Count("SELECT count(*) FROM users u JOIN cai_ranks rk ON rk.code=u.rank_code WHERE u.id=@id AND u.role='SOLDADO_ACTIVE' AND NOT u.reserve AND rk.level>=5",("id",sponsor))==0)) throw ApiException.Invalid("El padrino debe ser otro miembro activo de rango 5 o superior.");
                await db.Execute("UPDATE users SET parental_consent=@consent,sponsor_id=@sponsor,reserve=@reserve WHERE id=@id",("id",target),("consent",r.Flag("parentalConsentVerified")),("sponsor",sponsor),("reserve",r.Flag("reserve")));
                if(r.Flag("liftSanction")) {if(user.Role!="SUPER_ADMIN") throw ApiException.Forbidden(); await db.Execute("UPDATE users SET rank_ceiling=10 WHERE id=@id",("id",target));}
                if(!r.Flag("reserve")) {await db.Execute("DELETE FROM activity_alerts WHERE user_id=@id",("id",target)); await db.Execute("UPDATE users SET activity_resumed_at=now() WHERE id=@id",("id",target));}
            }
            await ModuleQueries.Audit(db,user,r.Action,target); return await RankSystem.Progress(db,target);
        }
        if(r.Action=="milestones.validate") return await Milestone(r,user,db,ct);
        if(r.Action=="activity.list") {user.Admin(); return await ModuleQueries.Page(db,r,"api_activity_alerts");}
        if(r.Action=="conduct.create")
        {
            var target=r.Id("targetId"); if(target==user.Id) throw ApiException.Invalid("Selecciona otro miembro.");
            var id=Guid.NewGuid();
            await db.Execute("INSERT INTO conduct_reports(id,reported_by,target_id,note) VALUES(@id,@actor,@target,@note)",("id",id),("actor",user.Id),("target",target),("note",r.Required("note",2000)));
            await ModuleQueries.Audit(db,user,r.Action,id); return new {id,status="PENDING"};
        }
        if(r.Action=="conduct.list") {user.Admin(); return await ModuleQueries.Page(db,r,"api_conduct");}
        user.Admin(); if(user.Role!="SUPER_ADMIN") throw ApiException.Forbidden();
        var record=r.Id();
        var report=await db.One("SELECT json_build_object('target',target_id)::text FROM conduct_reports WHERE id=@id",("id",record)) ?? throw ApiException.Missing();
        var member=report.GetProperty("target").GetGuid(); await RankSystem.Profile(db,member,true);
        if(member==user.Id) throw ApiException.Invalid("No puedes resolver un reporte sobre tu propia conducta.");
        var upheld=r.Flag("upheld");
        if(await db.Execute("UPDATE conduct_reports SET status=@status,resolution=@note,reviewed_by=@actor WHERE id=@id AND status='PENDING'",("id",record),("status",upheld ? "UPHELD" : "DISMISSED"),("note",r.Required("reviewNote",2000)),("actor",user.Id))==0) throw new ApiException(409,"ALREADY_REVIEWED","Reporte ya resuelto.");
        if(upheld) await db.Execute("UPDATE users SET rank_code='POSTULANTE',rank_ceiling=1 WHERE id=@id",("id",member));
        await ModuleQueries.Audit(db,user,r.Action,record); return new {id=record,status=upheld ? "UPHELD" : "DISMISSED"};
    }
    private static async Task<object> Milestone(ApiRequest r,Actor user,IDatabase db,CancellationToken ct)
    {
        user.Admin(); var target=r.Id("userId"); var code=r.Required("code",20);
        var h=await db.One("SELECT data::text FROM cai_milestones WHERE code=@code",("code",code)) ?? throw ApiException.Missing();
        var p=await RankSystem.Profile(db,target,true);
        if(code=="HIT-GM") {if(user.Role!="SUPER_ADMIN" || user.Id==target) throw ApiException.Forbidden();}
        else await RankSystem.Validator(db,user,target,code=="HIT-CAB" ? 7 : code=="HIT-ING" ? 6 : 0,r.Flag("foundingValidation"));
        if(code is "HIT-DOM" or "HIT-PROX" or "HIT-FUN") throw ApiException.Invalid("Este hito se verifica automáticamente al aprobar sus misiones llave.");
        if(!r.Flag("requirementsVerified")) throw ApiException.Invalid("Confirma cada requisito del hito y la autenticidad del acta.");
        var note=r.Required("reviewNote",2000);
        if(await db.Count("SELECT count(*) FROM user_milestones WHERE user_id=@id AND code=@code",("id",target),("code",code))>0) throw new ApiException(409,"MILESTONE_EXISTS","Hito ya validado.");
        var level=h.GetProperty("level").GetInt32();
        if(level>1 && p.GetProperty("level").GetInt32()<level-1) throw ApiException.Invalid("Completa primero los rangos anteriores.");
        if(code=="HIT-ING" && await db.Count("SELECT count(*) FROM users WHERE id=@id AND formation_started_at<=CURRENT_DATE-30",("id",target))==0) throw ApiException.Invalid("El ingreso requiere 30 días de formación.");
        if(code=="HIT-ING" && (!r.Flag("interviewVerified") || r.Optional("referenceMemberId",36) is null || r.Id("referenceMemberId")==target || await db.Count("SELECT count(*) FROM users WHERE id=@id AND role='SOLDADO_ACTIVE' AND NOT reserve",("id",r.Id("referenceMemberId")))==0)) throw ApiException.Invalid("Se requiere entrevista y referencia de otro miembro activo.");
        if(code=="HIT-CAB")
        {
            var months=await db.Count("SELECT count(DISTINCT date_trunc('month',occurred_at AT TIME ZONE 'America/Lima')) FROM mission_submissions WHERE user_id=@id AND status='APPROVED' AND NOT honor_report AND occurred_at>=((date_trunc('month',now() AT TIME ZONE 'America/Lima')-interval '2 months') AT TIME ZONE 'America/Lima')",("id",target));
            if(months<3 || !r.Flag("doctrinalExamVerified") || !r.Flag("ledFieldMissionVerified")) throw ApiException.Invalid("Se requieren examen integral, misión liderada y tres meses consecutivos de actividad.");
        }
        if(code=="HIT-COM" && (await RankSystem.Completed(db,target,"EST-02")<3 || !r.Flag("localCommandVerified"))) throw ApiException.Invalid("Se requieren tres EST-02 y una encomienda activa verificada.");
        if(code=="HIT-PRE" && (await RankSystem.Completed(db,target,"PRE-03")==0 || await RankSystem.Completed(db,target,"PRE-01")==0)) throw ApiException.Invalid("Completa PRE-03 y PRE-01.");
        if(code=="HIT-MAR" && (await RankSystem.Completed(db,target,"DEB-04")<3 || !r.Flag("distinctLocationsVerified"))) throw ApiException.Invalid("Se requieren tres DEB-04 en ciudades o barrios distintos.");
        if(code=="HIT-SEN" && (await RankSystem.Completed(db,target,"DEB-06")==0 || await RankSystem.Completed(db,target,"EST-03")==0)) throw ApiException.Invalid("Completa DEB-06 y la organización del debate EST-03.");
        if(code=="HIT-GM" && (user.Role!="SUPER_ADMIN" || !r.Flag("chapterElectionVerified") || await RankSystem.Completed(db,target,"EST-04")==0)) throw ApiException.Invalid("Se requiere elección del Capítulo y una obra de la Orden fundada y sostenida (EST-04). El voto no se registra ni otorga puntos.");
        var file=await FilesModule.Store(r,user,db,ct,"MISSION_EVIDENCE");
        if(await db.Count("SELECT count(*) FROM files WHERE id=@id AND content_type='application/pdf'",("id",file))==0) throw ApiException.Invalid("El acta del hito debe ser un PDF.");
        await db.Execute("INSERT INTO user_milestones(user_id,code,validated_by,file_id,note) VALUES(@user,@code,@actor,@file,@note)",("user",target),("code",code),("actor",user.Id),("file",file),("note",note));
        await RankSystem.Ledger(db,target,"milestone:"+code,"MILESTONE",h.GetProperty("points").GetDecimal(),h.GetProperty("name").GetString()!);
        await RankSystem.Recalculate(db,target); await ModuleQueries.Audit(db,user,r.Action,target);
        return await RankSystem.Progress(db,target);
    }
}
