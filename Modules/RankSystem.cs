using System.Text.Json;
using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public static class RankSystem
{
    public static async Task<JsonElement> Profile(IDatabase db, Guid id, bool locked = false)
    {
        // Read the joined rank only AFTER the row lock, so concurrent promotions cannot return a stale join.
        if(locked) await db.Execute("SELECT id FROM users WHERE id=@id FOR UPDATE",("id",id));
        return await db.One("SELECT json_build_object('level',r.level,'rankCode',u.rank_code,'reserve',u.reserve,'birthDate',u.birth_date,'parentalConsent',u.parental_consent,'sponsorId',u.sponsor_id,'ceiling',u.rank_ceiling)::text FROM users u JOIN cai_ranks r ON r.code=u.rank_code WHERE u.id=@id",("id",id)) ?? throw ApiException.Missing();
    }

    public static async Task<long> Completed(IDatabase db,Guid id,string code) => await db.Count("SELECT count(*) FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=@user AND s.status='APPROVED' AND NOT s.honor_report AND m.catalog_code=@code",("user",id),("code",code));

    public static async Task Ledger(IDatabase db,Guid id,string key,string kind,decimal points,string description,Guid? submission=null,DateTimeOffset? at=null) =>
        await db.Execute("INSERT INTO point_ledger(user_id,source_key,kind,points,description,submission_id,earned_at) VALUES(@user,@key,@kind,@points,@description,@submission,@at) ON CONFLICT DO NOTHING",("user",id),("key",key),("kind",kind),("points",points),("description",description),("submission",submission),("at",(at ?? DateTimeOffset.UtcNow).ToUniversalTime()));

    public static async Task Recalculate(IDatabase db, Guid id)
    {
        var profile = await Profile(db,id,true);
        foreach(var (code,ready) in new[] {
            ("HIT-DOM",await Completed(db,id,"PRX-01")>0 && await Completed(db,id,"PRX-02")>0),
            ("HIT-PROX",await Completed(db,id,"PRX-03")>0 && await Completed(db,id,"CAR-01")>0),
            ("HIT-FUN",await Completed(db,id,"FOR-03")>0 && await db.Count("SELECT count(DISTINCT s.module_code) FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=@id AND s.status='APPROVED' AND NOT s.honor_report AND m.catalog_code='FOR-04' AND s.module_code IN('CREDO','SACRAMENTOS','VIDA','ORACION')",("id",id))==4)
        })
        {
            if (!ready) continue;
            await db.Execute("INSERT INTO user_milestones(user_id,code,note) VALUES(@user,@code,'Requisitos verificados mediante misiones aprobadas') ON CONFLICT DO NOTHING",("user",id),("code",code));
            var h = await db.One("SELECT data::text FROM cai_milestones WHERE code=@code",("code",code));
            await Ledger(db,id,"milestone:"+code,"MILESTONE",h!.Value.GetProperty("points").GetDecimal(),code);
        }
        if(profile.GetProperty("reserve").GetBoolean()) return;
        var level=profile.GetProperty("level").GetInt32();
        for(var next=level+1;next<=profile.GetProperty("ceiling").GetInt32();next++)
        {
            var rank=(await db.One("SELECT data::text FROM cai_ranks WHERE level=@level",("level",next)))!.Value;
            var total=(await db.One("SELECT json_build_object('points',greatest(coalesce(sum(points),0),0))::text FROM point_ledger WHERE user_id=@id",("id",id)))!.Value.GetProperty("points").GetDecimal();
            if(total < rank.GetProperty("threshold").GetDecimal() || await db.Count("SELECT count(*) FROM user_milestones u JOIN cai_milestones h ON h.code=u.code WHERE u.user_id=@id AND h.level=@level",("id",id),("level",next))==0) break;
            if(await db.Count("SELECT count(*) FROM user_milestones WHERE user_id=@id AND code='HIT-ING'",("id",id))==0) break;
            if(next>=4 && await db.Count("SELECT count(*) FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=@id AND s.status='APPROVED' AND NOT s.honor_report AND m.catalog_code LIKE 'HOS-%' AND s.occurred_at>=now()-interval '90 days'",("id",id))==0) break;
            await db.Execute("UPDATE users SET rank_code=@code WHERE id=@id",("id",id),("code",rank.GetProperty("code").GetString()));
            await Ledger(db,id,"promotion:"+next,"PROMOTION",rank.GetProperty("bonus").GetDecimal(),"Ascenso a "+rank.GetProperty("name").GetString());
        }
    }

    public static async Task<object> Progress(IDatabase db,Guid id)
    {
        await Recalculate(db,id);
        return (await db.One("""
            SELECT json_build_object('rankCode',u.rank_code,'rank',r.data,'reserve',u.reserve,'birthDate',u.birth_date,'parentalConsent',u.parental_consent,
             'totalPoints',greatest(coalesce((SELECT sum(points) FROM point_ledger WHERE user_id=u.id),0),0),
             'totalBadgeWeight',greatest(coalesce((SELECT sum(points) FROM point_ledger WHERE user_id=u.id),0),0),
             'completedMissionTotal',(SELECT count(*) FROM mission_submissions WHERE user_id=u.id AND status='APPROVED'),
             'nextRank',(SELECT data FROM cai_ranks WHERE level=r.level+1),
             'nextMilestone',(SELECT h.data || CASE WHEN h.code='HIT-GM' THEN jsonb_build_object('requirement','Elección del Capítulo General, trayectoria completa y obra de la Orden fundada y sostenida.') ELSE '{}'::jsonb END || jsonb_build_object('completed',EXISTS(SELECT 1 FROM user_milestones um WHERE um.user_id=u.id AND um.code=h.code)) FROM cai_milestones h WHERE h.level=r.level+1),
             'entryApproved',EXISTS(SELECT 1 FROM user_milestones WHERE user_id=u.id AND code='HIT-ING'),
             'hospitalityCurrent',EXISTS(SELECT 1 FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=u.id AND s.status='APPROVED' AND m.catalog_code LIKE 'HOS-%' AND s.occurred_at>=now()-interval '90 days'),
             'pointsByArea',coalesce((SELECT json_agg(t) FROM(SELECT m.area,sum(s.points_awarded) AS points FROM mission_submissions s JOIN missions m ON m.id=s.mission_id WHERE s.user_id=u.id AND s.status='APPROVED' GROUP BY m.area)t),'[]'::json),
             'milestones',coalesce((SELECT json_agg(h.code) FROM user_milestones h WHERE h.user_id=u.id),'[]'::json))::text
            FROM users u JOIN cai_ranks r ON r.code=u.rank_code WHERE u.id=@id
            """,("id",id)))!.Value;
    }

    public static async Task Validator(IDatabase db,Actor actor,Guid target,int minimum=0,bool founding=false)
    {
        actor.Admin(); // Soldiers cannot read evidence or review it, regardless of rank.
        if(actor.Id==target) throw ApiException.Invalid("No puedes validar tu propia evidencia ni tu propio hito.");
        var established=await db.Count("SELECT count(*) FROM users u JOIN cai_ranks r ON r.code=u.rank_code WHERE u.role IN('SOLDADO_ACTIVE','REGISTRADOR','SUPER_ADMIN') AND NOT u.reserve AND r.level>=6");
        if(established<3) return; // Workbook's initial validation proposal.
        var a=await Profile(db,actor.Id); var b=await Profile(db,target);
        var level=a.GetProperty("level").GetInt32();
        var targetLevel=b.GetProperty("level").GetInt32();
        if(level<=targetLevel || level<minimum || (level==4 && targetLevel>2) || (level==6 && targetLevel>4) || level<4)
        {
            // Constituting the first higher ranks must remain possible without giving soldiers review access.
            if(founding && actor.Role=="SUPER_ADMIN" && await db.Count("SELECT count(*) FROM users u JOIN cai_ranks r ON r.code=u.rank_code WHERE u.role IN('SUPER_ADMIN','REGISTRADOR') AND NOT u.reserve AND r.level>@target AND r.level>=@minimum AND r.level>=4 AND (r.level<>4 OR @target<=2) AND (r.level<>6 OR @target<=4)",("target",targetLevel),("minimum",minimum))==0) return;
            throw ApiException.Forbidden();
        }
    }

    public static async Task<JsonElement> Eligible(IDatabase db,Actor actor,Guid missionId)
    {
        var m=await db.One("SELECT json_build_object('code',catalog_code,'minimumLevel',r.level,'field',field_mission,'honorAllowed',honor_allowed,'invitationRequired',invitation_required,'repeatLimit',repeat_limit,'cap',monthly_cap,'points',badge_weight,'category',category)::text FROM missions m JOIN cai_ranks r ON r.code=m.minimum_rank_code WHERE m.id=@id AND deleted_at IS NULL AND publication_state='PUBLISHED' FOR SHARE OF m",("id",missionId)) ?? throw ApiException.Missing();
        var p=await Profile(db,actor.Id,true);
        if(p.GetProperty("reserve").GetBoolean()) throw new ApiException(409,"IN_RESERVE","Estás en reserva. Solicita al administrador tu reincorporación.");
        var level=p.GetProperty("level").GetInt32(); var code=m.GetProperty("code").GetString();
        if(level==1 && m.GetProperty("field").GetBoolean()) throw ApiException.Forbidden();
        if(!actor.IsAdmin && level<m.GetProperty("minimumLevel").GetInt32() && !(level==1 && code=="PRX-02") && !(level==6 && code=="PRE-03")) throw ApiException.Forbidden();
        if(p.GetProperty("birthDate").ValueKind!=JsonValueKind.Null)
        {
            var birth=DateOnly.Parse(p.GetProperty("birthDate").GetString()!);
            if(birth>DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18) && (!p.GetProperty("parentalConsent").GetBoolean() || code is null || !(code.StartsWith("FOR-")||code.StartsWith("VIG-")||code.StartsWith("HOS-")))) throw ApiException.Forbidden();
        }
        return m;
    }
}
