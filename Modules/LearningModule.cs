using System.Text.Json;
using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class LearningModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["learning.get", "learning.update", "spiritual.list", "spiritual.record"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        var user=actor ?? throw ApiException.Forbidden();
        if(r.Action=="learning.get") return (await db.One("SELECT json_build_object('courseUrl',course_url,'waitingGroupUrl',waiting_group_url,'exams',exams)::text FROM learning_settings WHERE id=1"))!.Value;
        user.Active();
        if(r.Action=="learning.update")
        {
            user.Admin();
            var course=Link(r,"courseUrl"); var group=Link(r,"waitingGroupUrl");
            if(!r.Data.TryGetProperty("exams",out var exams) || exams.ValueKind!=JsonValueKind.Object) throw ApiException.Invalid("exams debe contener los cuestionarios por código.");
            foreach(var exam in exams.EnumerateObject())
            {
                if(!new[]{"FOR-03","CREDO","SACRAMENTOS","VIDA","ORACION"}.Contains(exam.Name) || exam.Value.ValueKind!=JsonValueKind.Array || exam.Value.GetArrayLength()>40) throw ApiException.Invalid("Cuestionario inválido; máximo 40 preguntas por examen.");
                if(exam.Name=="FOR-03" && exam.Value.GetArrayLength() is >0 and <20) throw ApiException.Invalid("FOR-03 requiere al menos 20 preguntas sobre los versículos clave.");
                foreach(var question in exam.Value.EnumerateArray())
                    if(question.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(question.GetString()) || question.GetString()!.Length>1000) throw ApiException.Invalid("Cada pregunta debe ser texto de hasta 1000 caracteres.");
            }
            await db.Execute("UPDATE learning_settings SET course_url=@course,waiting_group_url=@group,exams=@exams::jsonb,updated_at=now() WHERE id=1",("course",course),("group",group),("exams",exams.GetRawText()));
            await ModuleQueries.Audit(db,user,r.Action,user.Id);
            return new {saved=true};
        }
        if(r.Action=="spiritual.record")
        {
            var profile=await RankSystem.Profile(db,user.Id,true);
            if(profile.GetProperty("reserve").GetBoolean()) throw new ApiException(409,"IN_RESERVE","Solicita tu reincorporación antes de registrar actividad.");
            var kind=r.Choice("kind","PRAYER","PRAYER","ROSARY");
            if(!DateOnly.TryParseExact(r.Required("day",10),"yyyy-MM-dd",out var day) || day>DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)) || day<DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)).AddDays(-31)) throw ApiException.Invalid("Registra una fecha de los últimos 31 días, sin ser futura (America/Lima).");
            await db.Execute("INSERT INTO spiritual_records(user_id,kind,day) VALUES(@user,@kind,@day) ON CONFLICT DO NOTHING",("user",user.Id),("kind",kind),("day",day));
            return new {saved=true};
        }
        return new {items=await db.Many("SELECT json_build_object('kind',kind,'day',day)::text FROM spiritual_records WHERE user_id=@user AND day>=CURRENT_DATE-60 ORDER BY day DESC",("user",user.Id))};
    }
    private static string? Link(ApiRequest r,string name)
    {
        var value=r.Optional(name,2000);
        if(value is not null && (!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme!="https" || !string.IsNullOrEmpty(uri.UserInfo))) throw ApiException.Invalid("Los enlaces deben usar HTTPS.");
        return value;
    }
    public static async Task<JsonElement?> Exam(ApiRequest r,IDatabase db,string? code,string? module)
    {
        if(code is not ("FOR-03" or "FOR-04")) return null;
        var key=code=="FOR-03" ? code : module!;
        var exam=await db.One("SELECT (exams -> @key)::text FROM learning_settings WHERE id=1",("key",key));
        if(exam is null || exam.Value.ValueKind!=JsonValueKind.Array || exam.Value.GetArrayLength()==0) throw new ApiException(409,"EXAM_NOT_CONFIGURED","El administrador todavía no ha configurado este examen.");
        if(!r.Data.TryGetProperty("examAnswers",out var answers) || answers.ValueKind!=JsonValueKind.Array || answers.GetArrayLength()!=exam.Value.GetArrayLength()) throw ApiException.Invalid("Responde todas las preguntas del examen.");
        foreach(var answer in answers.EnumerateArray())
            if(answer.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(answer.GetString()) || answer.GetString()!.Length>2000) throw ApiException.Invalid("Cada respuesta es obligatoria y admite hasta 2000 caracteres.");
        return JsonSerializer.SerializeToElement(new {questions=exam.Value,answers});
    }
}
