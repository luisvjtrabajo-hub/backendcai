using Cai.Api.Api;
using Cai.Api.Infrastructure;

namespace Cai.Api.Modules;

public sealed class CertificatesModule : IActionHandler
{
    public IReadOnlyCollection<string> Actions { get; } = ["certificates.list", "certificates.create", "activation.claim", "activation.submit", "activation.status", "certificateReviews.list", "certificateReviews.review"];
    public async Task<object> Handle(ApiRequest r, Actor? actor, IDatabase db, CancellationToken ct)
    {
        if (actor is null) throw ApiException.Forbidden();
        if (r.Action == "activation.status")
            return new { user = await ModuleQueries.Get(db, "api_users", actor.Id), reviews = await ModuleQueries.Page(db, r, "api_certificate_reviews", "\"userId\"=@user", ("user", actor.Id)) };
        if (r.Action is "activation.claim" or "activation.submit")
        {
            if (actor.Role != "SOLDADO_PENDING") throw ApiException.Forbidden();
            // Serializa intentos de activación y aprobación para el mismo usuario.
            await db.Execute("SELECT id FROM users WHERE id=@id FOR UPDATE", ("id", actor.Id));
            var current = await db.One("SELECT json_build_object('role',role,'fullName',full_name,'email',email)::text FROM users WHERE id=@id", ("id", actor.Id));
            if (current!.Value.GetProperty("role").GetString() != "SOLDADO_PENDING") throw new ApiException(409, "ALREADY_ACTIVATED", "La cuenta ya fue procesada.");
            var number = r.Required("certificateNumber", 100).ToUpperInvariant();
            if (r.Action == "activation.submit")
            {
                var fileId = await FilesModule.Store(r, actor, db, ct);
                var reviewId = Guid.NewGuid();
                await db.Execute("INSERT INTO certificate_reviews(id,user_id,certificate_number,file_id) VALUES(@id,@user,@number,@file)", ("id", reviewId), ("user", actor.Id), ("number", number), ("file", fileId));
                return new { id = reviewId, status = "PENDING", message = "Certificado enviado para revisión." };
            }
            // Bloqueo de fila: un certificado no puede consumirse por dos cuentas.
            var certificate = await db.One("SELECT json_build_object('id',id,'name',issued_to_name,'email',issued_to_email,'used',used_by_user_id)::text FROM certificates WHERE certificate_number=@number FOR UPDATE", ("number", number));
            if (certificate is null || certificate.Value.GetProperty("used").ValueKind != System.Text.Json.JsonValueKind.Null)
                throw new ApiException(409, "CERTIFICATE_UNAVAILABLE", "El certificado no existe o ya fue utilizado.");
            var boundEmail = certificate.Value.GetProperty("email").GetString();
            var boundName = certificate.Value.GetProperty("name").GetString();
            if (boundEmail is not null ? !string.Equals(boundEmail,current.Value.GetProperty("email").GetString(),StringComparison.OrdinalIgnoreCase) :
                boundName is null || !string.Equals(boundName.Trim(),current.Value.GetProperty("fullName").GetString()?.Trim(),StringComparison.OrdinalIgnoreCase))
                throw new ApiException(409, "CERTIFICATE_OWNER_MISMATCH", "El titular no coincide. Envía una foto para revisión manual.");
            await db.Execute("UPDATE certificates SET used_by_user_id=@user,used_at=now() WHERE id=@id", ("user", actor.Id), ("id", certificate.Value.GetProperty("id").GetGuid()));
            await db.Execute("UPDATE users SET role='SOLDADO_ACTIVE' WHERE id=@id", ("id", actor.Id));
            await db.Execute("UPDATE certificate_reviews SET status='APPROVED',reviewed_at=now(),review_note='Activación por certificado' WHERE user_id=@id AND status='PENDING'", ("id", actor.Id));
            await ModuleQueries.Audit(db, actor, r.Action, certificate.Value.GetProperty("id").GetGuid());
            return new { message = "Cuenta activada.", user = await ModuleQueries.Get(db, "api_users", actor.Id) };
        }
        actor.Admin();
        if (r.Action == "certificates.list") return await ModuleQueries.Page(db, r, "api_certificates");
        if (r.Action == "certificateReviews.list")
        {
            var status = r.Choice("status", "PENDING", "PENDING", "APPROVED", "REJECTED");
            return await ModuleQueries.Page(db, r, "api_certificate_reviews", "status=@status", ("status", status));
        }
        if (r.Action == "certificates.create")
        {
            var id = Guid.NewGuid();
            var email = r.Optional("issuedToEmail", 254);
            await db.Execute("INSERT INTO certificates(id,certificate_number,issued_to_name,issued_to_email,created_by_user_id) VALUES(@id,@number,@name,@email,@actor)",
                ("id", id), ("number", r.Required("certificateNumber",100).ToUpperInvariant()), ("name", r.Optional("issuedToName",160)), ("email", email is null ? null : AuthModule.Email(email)), ("actor", actor.Id));
            await ModuleQueries.Audit(db, actor, r.Action, id);
            return await ModuleQueries.Get(db, "api_certificates", id);
        }
        var reviewIdToUpdate = r.Id();
        // Siempre bloquear usuario antes de revisión para mantener el orden de bloqueos.
        var review = await db.One("SELECT json_build_object('userId',user_id)::text FROM certificate_reviews WHERE id=@id", ("id", reviewIdToUpdate)) ?? throw ApiException.Missing();
        var userId = review.GetProperty("userId").GetGuid();
        var targetUser = await db.One("SELECT json_build_object('role',role)::text FROM users WHERE id=@user FOR UPDATE", ("user", userId)) ?? throw ApiException.Missing();
        if (targetUser.GetProperty("role").GetString() != "SOLDADO_PENDING") throw new ApiException(409, "INVALID_STATE", "El usuario ya no está pendiente.");
        var decision = r.Choice("status", "APPROVED", "APPROVED", "REJECTED");
        if (await db.Execute("UPDATE certificate_reviews SET status=@status,review_note=@note,reviewed_by_user_id=@actor,reviewed_at=now() WHERE id=@id AND status='PENDING'", ("status", decision), ("note", r.Optional("reviewNote",2000)), ("actor", actor.Id), ("id", reviewIdToUpdate)) == 0)
            throw new ApiException(409, "ALREADY_REVIEWED", "La solicitud ya fue revisada.");
        await db.Execute("UPDATE users SET role=@role WHERE id=@user", ("role", decision == "APPROVED" ? "SOLDADO_ACTIVE" : "SOLDADO_INACTIVE"), ("user", userId));
        if (decision == "REJECTED") await db.Execute("DELETE FROM sessions WHERE user_id=@user", ("user", userId));
        await ModuleQueries.Audit(db, actor, r.Action, reviewIdToUpdate);
        return await ModuleQueries.Get(db, "api_certificate_reviews", reviewIdToUpdate);
    }
}
