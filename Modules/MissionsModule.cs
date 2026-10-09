using System.Text.Json;
using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class MissionsModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["missions.list","missions.assign","assignments.list","missions.create","missions.update","missions.publish","missions.archive","missions.delete","submissions.create","submissions.list","submissions.review","progress.get","history.get"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        var user=actor ?? throw ApiException.Forbidden(); user.Active();
        if(r.Action=="missions.list")
            return await ModuleQueries.PageProjected(db,r,"api_missions",user.IsAdmin ? "TRUE" : "(\"publicationState\"='PUBLISHED' AND EXISTS(SELECT 1 FROM users u JOIN cai_ranks ur ON ur.code=u.rank_code JOIN cai_ranks mr ON mr.code=v.\"minimumRankCode\" JOIN missions mm ON mm.id=v.id WHERE u.id=@user AND (mr.level<=ur.level OR (ur.level=1 AND mm.catalog_code='PRX-02') OR (ur.level=6 AND mm.catalog_code='PRE-03')) AND (u.birth_date IS NULL OR u.birth_date<=CURRENT_DATE-interval '18 years' OR (u.parental_consent AND mm.catalog_code SIMILAR TO '(FOR|VIG|HOS)-%')))) OR EXISTS(SELECT 1 FROM missions own WHERE own.id=v.id AND own.created_by_user_id=@user)",
                """
                v.*,(SELECT json_build_object('code',m.catalog_code,'category',m.category,'area',m.area,'evidence',m.evidence_requirement,'repeatLimit',m.repeat_limit,'monthlyCap',m.monthly_cap,'field',m.field_mission,'honorAllowed',m.honor_allowed,'invitationRequired',m.invitation_required) FROM missions m WHERE m.id=v.id) AS rules,
                (SELECT json_build_object('id',a.id,'status',coalesce((SELECT s.status FROM mission_submissions s WHERE s.mission_id=a.mission_id AND s.user_id=a.user_id ORDER BY s.created_at DESC,s.id DESC LIMIT 1),'ASSIGNED')) FROM mission_assignments a WHERE a.mission_id=v.id AND a.user_id=@user) AS "myAssignment"
                """,("user",user.Id));
        if(r.Action=="assignments.list")
        {
            var where=user.IsAdmin ? "TRUE" : "\"userId\"=@user"; var args=new List<(string,object?)>();
            if(!user.IsAdmin) args.Add(("user",user.Id));
            if(r.Optional("missionId",36) is not null) {where+=" AND \"missionId\"=@mission";args.Add(("mission",r.Id("missionId")));}
            return await ModuleQueries.Page(db,r,"api_assignments",where,[..args]);
        }
        if(r.Action=="missions.assign")
        {
            var id=r.Id(); await RankSystem.Eligible(db,user,id);
            if(r.File is not null) throw ApiException.Invalid("La asignación no requiere archivo.");
            var n=await db.Execute("INSERT INTO mission_assignments(mission_id,user_id) VALUES(@mission,@user) ON CONFLICT DO NOTHING",("mission",id),("user",user.Id));
            var a=(await db.One("SELECT json_build_object('id',id,'missionId',mission_id,'userId',user_id,'status',coalesce((SELECT s.status FROM mission_submissions s WHERE s.mission_id=a.mission_id AND s.user_id=a.user_id ORDER BY s.created_at DESC,s.id DESC LIMIT 1),'ASSIGNED'))::text FROM mission_assignments a WHERE mission_id=@mission AND user_id=@user",("mission",id),("user",user.Id)))!.Value;
            if(n>0) await ModuleQueries.Audit(db,user,r.Action,a.GetProperty("id").GetGuid());
            return a;
        }
        if(r.Action=="submissions.list")
        {
            user.Admin(); var where="TRUE"; var args=new List<(string,object?)>();
            if(r.Optional("status",20) is not null) {where+=" AND v.status=@status"; args.Add(("status",r.Choice("status","PENDING","PENDING","APPROVED","REJECTED")));}
            if(r.Optional("missionId",36) is not null) {where+=" AND v.\"missionId\"=@mission"; args.Add(("mission",r.Id("missionId")));}
            return await ModuleQueries.PageProjected(db,r,"api_submissions",where,"v.*,(SELECT json_build_object('moduleCode',s.module_code,'occurredAt',s.occurred_at,'details',s.details,'honorReport',s.honor_report,'pointsAwarded',s.points_awarded) FROM mission_submissions s WHERE s.id=v.id) AS report",[..args]);
        }
        if(r.Action=="progress.get") return await RankSystem.Progress(db,user.Id);
        if(r.Action=="history.get") return new { completedMissionTotal=await db.Count("SELECT count(*) FROM mission_submissions WHERE user_id=@user AND status='APPROVED'",("user",user.Id)),history=await ModuleQueries.Page(db,r,"api_submission_status","\"userId\"=@user",("user",user.Id)) };
        if(r.Action=="submissions.create") return await Submit(r,user,db,ct);
        if(r.Action=="submissions.review") return await Review(r,user,db);
        if(r.Action is "missions.create" or "missions.update")
        {
            var profile=await RankSystem.Profile(db,user.Id);
            if(!user.IsAdmin && (profile.GetProperty("level").GetInt32()<5 || profile.GetProperty("reserve").GetBoolean())) throw ApiException.Forbidden();
            var id=r.Action=="missions.create" ? Guid.NewGuid() : r.Id(); var rank=r.Optional("minimumRankCode",30) ?? "CABALLERO_TEMPLE";
            if(await db.Count("SELECT count(*) FROM cai_ranks WHERE code=@code",("code",rank))==0) throw ApiException.Invalid("Rango inválido.");
            var type=r.Choice("missionType","OPERACIONAL","OPERACIONAL","FORMATIVA","ESPIRITUAL");
            if((type=="OPERACIONAL" || r.Flag("fieldMission")) && rank=="POSTULANTE") throw ApiException.Invalid("Postulante no sale a campo. Selecciona Compañero de Armas o un rango superior.");
            var args=new (string,object?)[] {("id",id),("actor",user.Id),("title",r.Required("title",180)),("description",r.Required("description",4000)),("type",type),("rank",rank),("points",r.Number("badgeWeight",1,1,1000)),("field",type=="OPERACIONAL" || r.Flag("fieldMission")),("evidence",r.Required("evidenceRequirement",2000))};
            if(r.Action=="missions.create") await db.Execute("INSERT INTO missions(id,title,description,mission_type,minimum_rank_code,badge_weight,field_mission,evidence_requirement,created_by_user_id,repeat_limit) VALUES(@id,@title,@description,@type,@rank,@points,@field,@evidence,@actor,1)",args);
            else if(await db.Execute("UPDATE missions SET title=@title,description=@description,mission_type=@type,minimum_rank_code=@rank,badge_weight=@points,field_mission=@field,evidence_requirement=@evidence WHERE id=@id AND deleted_at IS NULL AND publication_state='DRAFT' AND catalog_code IS NULL AND (created_by_user_id=@actor OR @admin)",[..args,("admin",user.IsAdmin)])==0) throw new ApiException(409,"INVALID_STATE","Solo se pueden editar borradores propios; el catálogo oficial conserva las reglas del archivo.");
            await ModuleQueries.Audit(db,user,r.Action,id); return await ModuleQueries.Get(db,"api_missions",id);
        }
        user.Admin(); var mission=r.Id();
        if(r.Action=="missions.delete")
        {
            if(await db.Execute("UPDATE missions SET deleted_at=now(),publication_state='ARCHIVED' WHERE id=@id AND deleted_at IS NULL",("id",mission))==0) throw ApiException.Missing();
            await ModuleQueries.Audit(db,user,r.Action,mission);
            return new { id=mission,deleted=true };
        }
        var changed=r.Action=="missions.publish" ? await db.Execute("UPDATE missions SET publication_state='PUBLISHED',published_at=now() WHERE id=@id AND deleted_at IS NULL AND publication_state='DRAFT'",("id",mission)) : await db.Execute("UPDATE missions SET publication_state='ARCHIVED' WHERE id=@id AND deleted_at IS NULL AND publication_state IN('DRAFT','PUBLISHED')",("id",mission));
        if(changed==0) throw new ApiException(409,"INVALID_STATE","La misión no admite esta transición.");
        await ModuleQueries.Audit(db,user,r.Action,mission); return await ModuleQueries.Get(db,"api_missions",mission);
    }
    private static async Task<object> Submit(ApiRequest r,Actor user,IDatabase db,CancellationToken ct)
    {
        var mission=r.Id("missionId"); var m=await RankSystem.Eligible(db,user,mission); var p=await RankSystem.Profile(db,user.Id);
        if(p.GetProperty("birthDate").ValueKind==JsonValueKind.Null) throw ApiException.Invalid("Completa tu fecha de nacimiento antes de reportar misiones.");
        if(!r.Flag("respectConfirmed") || !r.Flag("privacyConfirmed")) throw ApiException.Invalid("Confirma el respeto, la privacidad y la anonimización de tu reporte.");
        var note=r.Required("submissionNote",2000); var honor=r.Flag("honorReport") || (m.GetProperty("code").GetString() is ("VIG-02" or "VIG-03" or "VIG-05") && r.File is null && r.Optional("evidenceUrl",2000) is null);
        if(honor && !m.GetProperty("honorAllowed").GetBoolean()) throw ApiException.Invalid("Esta misión exige evidencia verificable.");
        var url=r.Optional("evidenceUrl",2000);
        if(url is not null && (!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo))) throw ApiException.Invalid("El enlace de evidencia debe usar HTTPS.");
        if(r.Flag("recordingIncluded") && !r.Flag("recordingConsent")) throw ApiException.Invalid("Una grabación exige consentimiento explícito. Presenta una bitácora escrita si no lo tienes.");
        var occurred=ReadTime(r,"occurredAt"); Guid? companion=r.Optional("companionId",36) is null ? null : r.Id("companionId");
        if(companion is not null && (companion==user.Id || await db.Count("SELECT count(*) FROM users WHERE id=@id AND role='SOLDADO_ACTIVE' AND NOT reserve",("id",companion))==0)) throw ApiException.Invalid("Selecciona otro compañero activo.");
        if(m.GetProperty("field").GetBoolean())
        {
            companion=r.Id("companionId");
            if(companion==user.Id || await db.Count("SELECT count(*) FROM users WHERE id=@id AND role='SOLDADO_ACTIVE' AND NOT reserve",("id",companion))==0) throw ApiException.Invalid("Selecciona otro compañero activo.");
            var local=occurred.ToOffset(TimeSpan.FromHours(-5)); var end=ReadTime(r,"endedAt"); var localEnd=end.ToOffset(TimeSpan.FromHours(-5));
            if(end<occurred || local.Hour<6 || local.Hour>=20 || localEnd.Hour<6 || localEnd.Hour>=20 || local.Date!=localEnd.Date) throw ApiException.Invalid("Las misiones de campo deben realizarse entre las 06:00 y las 20:00 (America/Lima).");
            if(!r.Flag("safeFieldConfirmed") || !r.Flag("noVulnerableTargets")) throw ApiException.Invalid("Confirma un entorno seguro, el permiso de acceso y que no se abordó a menores ni personas vulnerables.");
            var cp=await RankSystem.Profile(db,companion.Value);
            if(p.GetProperty("level").GetInt32()<=3 && cp.GetProperty("level").GetInt32()<4) throw ApiException.Invalid("Los rangos iniciales necesitan un compañero de rango 4 o superior.");
            if(DateOnly.Parse(p.GetProperty("birthDate").GetString()!)>DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18) && (cp.GetProperty("level").GetInt32()<5 || cp.GetProperty("birthDate").ValueKind==JsonValueKind.Null || DateOnly.Parse(cp.GetProperty("birthDate").GetString()!)>DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18))) throw ApiException.Invalid("Un menor necesita un acompañante adulto de rango 5 o superior.");
        }
        Guid? invitation=null;
        if(m.GetProperty("invitationRequired").GetBoolean())
        {
            invitation=r.Id("invitationFileId");
            if(await db.Count("SELECT count(*) FROM files WHERE id=@id AND owner_user_id=@user AND purpose='MISSION_EVIDENCE' AND content_type='application/pdf'",("id",invitation),("user",user.Id))==0) throw ApiException.Invalid("Adjunta antes la invitación escrita en PDF.");
        }
        if(await db.Count("SELECT count(*) FROM mission_assignments WHERE mission_id=@mission AND user_id=@user",("mission",mission),("user",user.Id))==0) throw new ApiException(409,"MISSION_NOT_ASSIGNED","Asígnate la misión antes de reportarla.");
        if(await db.Count("SELECT count(*) FROM mission_submissions WHERE mission_id=@mission AND user_id=@user AND status='PENDING'",("mission",mission),("user",user.Id))>0) throw new ApiException(409,"SUBMISSION_EXISTS","Ya hay un reporte pendiente.");
        if(m.GetProperty("repeatLimit").ValueKind!=JsonValueKind.Null && await db.Count("SELECT count(*) FROM mission_submissions WHERE mission_id=@mission AND user_id=@user AND status='APPROVED'",("mission",mission),("user",user.Id))>=m.GetProperty("repeatLimit").GetInt32()) throw new ApiException(409,"REPEAT_LIMIT","Ya completaste las repeticiones permitidas.");
        var code=m.GetProperty("code").GetString(); var module=r.Optional("moduleCode",30);
        if(code=="VIG-02" && await db.Count("SELECT count(*) FROM mission_submissions WHERE mission_id=@mission AND user_id=@user AND status='APPROVED' AND date_trunc('month',occurred_at AT TIME ZONE 'America/Lima')=date_trunc('month',@at::timestamptz AT TIME ZONE 'America/Lima')",("mission",mission),("user",user.Id),("at",occurred.ToUniversalTime()))>0) throw new ApiException(409,"MONTHLY_REPEAT_LIMIT","La confesión del mes solo puede reportarse una vez al mes.");
        if(code=="FOR-04")
        {
            module=r.Choice("moduleCode","","CREDO","SACRAMENTOS","VIDA","ORACION");
            if(await db.Count("SELECT count(*) FROM mission_submissions WHERE mission_id=@mission AND user_id=@user AND module_code=@module AND status='APPROVED'",("mission",mission),("user",user.Id),("module",module))>0) throw new ApiException(409,"MODULE_COMPLETED","Ese módulo ya está aprobado.");
        }
        var exam = await LearningModule.Exam(r,db,code,module);
        if(code is "VIG-01" or "VIG-06")
        {
            var day=DateOnly.FromDateTime(occurred.ToOffset(TimeSpan.FromHours(-5)).DateTime);
            if(code=="VIG-01" && await db.Count("SELECT count(*) FROM spiritual_records WHERE user_id=@user AND kind='PRAYER' AND day BETWEEN @day::date-6 AND @day::date",("user",user.Id),("day",day))!=7) throw ApiException.Invalid("VIG-01 requiere siete días consecutivos de oración registrados en la aplicación.");
            if(code=="VIG-06" && await db.Count("SELECT count(*) FROM spiritual_records WHERE user_id=@user AND kind='ROSARY' AND date_trunc('week',day::timestamp)=date_trunc('week',@day::date::timestamp)",("user",user.Id),("day",day))==0) throw ApiException.Invalid("Registra primero el rosario de esa semana en la aplicación.");
            var period=code=="VIG-01" ? "day" : "week";
            if(await db.Count($"SELECT count(*) FROM mission_submissions WHERE user_id=@user AND mission_id=@mission AND status='APPROVED' AND date_trunc('{period}',occurred_at AT TIME ZONE 'America/Lima')=date_trunc('{period}',@day::date::timestamp)",("user",user.Id),("mission",mission),("day",day))>0) throw new ApiException(409,"SPIRITUAL_PERIOD_COMPLETED","Ese registro de oración o rosario ya fue acreditado.");
        }
        Guid? file=null; if(r.File is not null) file=await FilesModule.Store(r,user,db,ct,"MISSION_EVIDENCE");
        if(!honor && file is null && url is null && note.Length<30) throw ApiException.Invalid("Adjunta evidencia o escribe una bitácora detallada de al menos 30 caracteres.");
        if(code=="VIG-05" && await db.Count("SELECT count(*) FROM missions WHERE id=@id",("id",r.Id("linkedMissionId")))==0) throw ApiException.Invalid("La misión vinculada no existe.");
        Guid? registry=null;
        if(code=="CAR-01")
        {
            registry=r.Id("sectReportId");
            if(await db.Count("SELECT count(*) FROM sect_reports WHERE id=@id AND reported_by_user_id=@user AND status='APPROVED' AND latitude IS NOT NULL AND longitude IS NOT NULL",("id",registry),("user",user.Id))==0) throw ApiException.Invalid("CAR-01 exige una ficha propia aprobada, con doctrina y coordenadas en el mapa.");
            if(await db.Count("SELECT count(*) FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=@user AND s.status='APPROVED' AND m.catalog_code='CAR-01' AND s.details->>'sectReportId'=@registry",("user",user.Id),("registry",registry.Value.ToString()))>0) throw new ApiException(409,"REGISTRY_COMPLETED","Esta ficha ya fue acreditada en CAR-01.");
            if(file is null || await db.Count("SELECT count(*) FROM files WHERE id=@id AND content_type LIKE 'image/%'",("id",file))==0) throw ApiException.Invalid("CAR-01 exige una fotografía de fachada.");
        }
        var id=Guid.NewGuid(); var details=JsonSerializer.Serialize(new { exam,evidenceUrl=url,companionId=companion,invitationFileId=invitation,sectReportId=registry,recordingIncluded=r.Flag("recordingIncluded"),recordingConsent=r.Flag("recordingConsent"),respectConfirmed=true,privacyConfirmed=true,safeFieldConfirmed=r.Flag("safeFieldConfirmed"),noVulnerableTargets=r.Flag("noVulnerableTargets"),endedAt=r.Optional("endedAt",40),linkedMissionId=r.Optional("linkedMissionId",36),mentionsMinors=r.Flag("mentionsMinors") });
        await db.Execute("INSERT INTO mission_submissions(id,mission_id,user_id,file_id,submission_note,module_code,occurred_at,details,honor_report) VALUES(@id,@mission,@user,@file,@note,@module,@at,@details::jsonb,@honor)",("id",id),("mission",mission),("user",user.Id),("file",file),("note",note),("module",module),("at",occurred.ToUniversalTime()),("details",details),("honor",honor));
        return await ModuleQueries.Get(db,user.IsAdmin ? "api_submissions" : "api_submission_status",id);
    }
    private static DateTimeOffset ReadTime(ApiRequest r,string name)
    {
        var text=r.Required(name,40);
        if(!System.Text.RegularExpressions.Regex.IsMatch(text,@"(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(text,out var value) || value>DateTimeOffset.UtcNow.AddMinutes(5)) throw ApiException.Invalid(name+" debe indicar fecha, hora y zona horaria, sin ser futura.");
        return value;
    }
    private static async Task<object> Review(ApiRequest r,Actor user,IDatabase db)
    {
        user.Admin(); var id=r.Id();
        var s=await db.One("SELECT json_build_object('userId',s.user_id,'missionId',s.mission_id,'honor',s.honor_report,'at',s.occurred_at,'details',s.details,'points',m.badge_weight,'cap',m.monthly_cap,'code',m.catalog_code)::text FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.id=@id",("id",id)) ?? throw ApiException.Missing();
        var target=s.GetProperty("userId").GetGuid(); await RankSystem.Profile(db,target,true);
        if(r.Flag("foundingValidation")) r.Required("reviewNote",2000);
        await RankSystem.Validator(db,user,target,s.GetProperty("code").GetString() is "PRX-04" or "HOS-03" ? 6 : 0,r.Flag("foundingValidation"));
        var status=r.Choice("status","APPROVED","APPROVED","REJECTED"); var reason=r.Choice("rejectionReason","OTHER","OTHER","DISRESPECT","FALSE_EVIDENCE");
        if(status=="REJECTED") r.Required("reviewNote",2000);
        if(status=="APPROVED" && !r.Flag("requirementsVerified")) throw ApiException.Invalid("Confirma que la evidencia cumple todos los requisitos de la misión.");
        if(await db.Execute("UPDATE mission_submissions SET status=@status,review_note=@note,reviewed_by_user_id=@actor,reviewed_at=now() WHERE id=@id AND status='PENDING'",("id",id),("status",status),("note",r.Optional("reviewNote",2000)),("actor",user.Id))==0) throw new ApiException(409,"ALREADY_REVIEWED","El reporte ya fue revisado.");
        var at=s.GetProperty("at").GetDateTimeOffset();
        if(status=="APPROVED")
        {
            var points=s.GetProperty("points").GetDecimal()*(s.GetProperty("honor").GetBoolean() ? .5m : 1m);
            if(r.Flag("excellent") && !s.GetProperty("honor").GetBoolean()) points*=1.2m;
            if(r.Flag("teamBonus"))
            {
                var details=s.GetProperty("details");
                if(!details.TryGetProperty("companionId",out var companion) || companion.ValueKind==JsonValueKind.Null) throw ApiException.Invalid("El bono de equipo requiere un compañero registrado.");
                var cp=await RankSystem.Profile(db,companion.GetGuid()); var tp=await RankSystem.Profile(db,target);
                if(cp.GetProperty("sponsorId").ValueKind==JsonValueKind.Null || cp.GetProperty("sponsorId").GetGuid()!=target || cp.GetProperty("level").GetInt32()>=tp.GetProperty("level").GetInt32()) throw ApiException.Invalid("El compañero debe ser tu apadrinado y de rango inferior.");
                points+=15;
            }
            Guid? firstRegistry=null; decimal firstBonus=0;
            if(r.Flag("firstRegistryBonus"))
            {
                if(s.GetProperty("code").GetString()!="CAR-01") throw ApiException.Invalid("El bono de primicia corresponde a CAR-01.");
                var report=r.Id("sectReportId");
                if(s.GetProperty("details").GetProperty("sectReportId").GetGuid()!=report) throw ApiException.Invalid("La primicia debe corresponder a la ficha del reporte.");
                if(await db.Count("SELECT count(*) FROM sect_reports WHERE id=@id AND reported_by_user_id=@user AND status='APPROVED'",("id",report),("user",target))==0) throw ApiException.Invalid("Se requiere una ficha nueva aprobada del miembro.");
                if(await db.Count("SELECT count(*) FROM point_ledger WHERE source_key=@key",("key","firstRegistry:"+report))>0) throw ApiException.Invalid("La primicia ya fue premiada.");
                if(await db.Count("SELECT count(*) FROM sect_reports other JOIN sect_reports current ON current.id=@id WHERE other.id<>current.id AND other.status='APPROVED' AND other.created_at<current.created_at AND lower(other.sect_name)=lower(current.sect_name) AND lower(other.location_description)=lower(current.location_description)",("id",report))>0) throw ApiException.Invalid("Esta ficha ya estaba registrada.");
                firstRegistry=report; firstBonus=25;
            }
            if(s.GetProperty("cap").ValueKind!=JsonValueKind.Null)
            {
                var spent=(await db.One("SELECT json_build_object('points',coalesce(sum(points_awarded),0))::text FROM mission_submissions WHERE user_id=@user AND mission_id=@mission AND status='APPROVED' AND id<>@id AND date_trunc('month',occurred_at AT TIME ZONE 'America/Lima')=date_trunc('month',@at::timestamptz AT TIME ZONE 'America/Lima')",("user",target),("mission",s.GetProperty("missionId").GetGuid()),("id",id),("at",at.ToUniversalTime())))!.Value.GetProperty("points").GetDecimal();
                var available=Math.Max(0,s.GetProperty("cap").GetDecimal()-spent);
                points=Math.Min(points,available); firstBonus=Math.Min(firstBonus,Math.Max(0,available-points));
            }
            await db.Execute("UPDATE mission_submissions SET points_awarded=@points WHERE id=@id",("id",id),("points",points+firstBonus));
            await RankSystem.Ledger(db,target,"submission:"+id,"MISSION",points,"Misión "+s.GetProperty("code").GetString(),id,at);
            if(firstRegistry is not null) await RankSystem.Ledger(db,target,"firstRegistry:"+firstRegistry,"FIRST_REGISTRY",firstBonus,"Primera ficha de una secta",id,at);
            var weeks=await db.Count("SELECT count(DISTINCT date_trunc('week',occurred_at AT TIME ZONE 'America/Lima')) FROM mission_submissions WHERE user_id=@user AND status='APPROVED' AND occurred_at>=((date_trunc('week',now() AT TIME ZONE 'America/Lima')-interval '3 weeks') AT TIME ZONE 'America/Lima') AND occurred_at<((date_trunc('week',now() AT TIME ZONE 'America/Lima')+interval '1 week') AT TIME ZONE 'America/Lima')",("user",target));
            if(weeks>=4 && await db.Count("SELECT count(*) FROM point_ledger WHERE user_id=@id AND kind='CONSISTENCY' AND created_at>now()-interval '28 days'",("id",target))==0) await RankSystem.Ledger(db,target,"consistency:"+DateTime.UtcNow.ToString("yyyy-MM-dd"),"CONSISTENCY",25,"Cuatro semanas consecutivas con misiones validadas");
        }
        else if(reason=="DISRESPECT") await RankSystem.Ledger(db,target,"penalty:"+id,"DISRESPECT",-20,"Reporte irrespetuoso",id);
        else if(reason=="FALSE_EVIDENCE")
        {
            var month=(await db.One("SELECT json_build_object('points',greatest(coalesce(sum(points),0),0))::text FROM point_ledger WHERE user_id=@user AND date_trunc('month',earned_at AT TIME ZONE 'America/Lima')=date_trunc('month',@at::timestamptz AT TIME ZONE 'America/Lima')",("user",target),("at",at.ToUniversalTime())))!.Value.GetProperty("points").GetDecimal();
            await RankSystem.Ledger(db,target,"false:"+id,"FALSE_EVIDENCE",-month,"Pérdida de los puntos del mes por evidencia falsa",id,at);
            if(await db.Count("SELECT count(*) FROM point_ledger WHERE user_id=@id AND kind='FALSE_EVIDENCE'",("id",target))>=2)
                await db.Execute("UPDATE users u SET rank_code=r.code,rank_ceiling=r.level FROM cai_ranks old JOIN cai_ranks r ON r.level=greatest(old.level-1,1) WHERE u.id=@id AND old.code=u.rank_code",("id",target));
        }
        await RankSystem.Recalculate(db,target); await ModuleQueries.Audit(db,user,r.Action,id);
        return await ModuleQueries.Get(db,"api_submissions",id);
    }
}
