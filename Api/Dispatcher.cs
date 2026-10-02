using Cai.Api.Infrastructure;
using Cai.Api.Modules;

namespace Cai.Api.Api;

public interface IActionHandler
{
    IReadOnlyCollection<string> Actions { get; }
    Task<object> Handle(ApiRequest request, Actor? actor, IDatabase db, CancellationToken ct);
}

public sealed class Dispatcher(IEnumerable<IActionHandler> handlers)
{
    private readonly Dictionary<string, IActionHandler> routes = handlers.SelectMany(h => h.Actions.Select(a => (a, h))).ToDictionary(x => x.a, x => x.h, StringComparer.Ordinal);
    public async Task<object> Execute(ApiRequest request, string? authorization, IDatabase db, CancellationToken ct)
    {
        if (!routes.TryGetValue(request.Action, out var handler)) throw new ApiException(400, "UNKNOWN_ACTION", "Acción desconocida. Consulta docs/API.md.");
        Actor? actor = null;
        if (request.Action is not ("auth.login" or "auth.register"))
        {
            var token = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? authorization[7..] : null;
            if (token is null || token.Length != 64) throw new ApiException(401, "UNAUTHORIZED", "Inicia sesión para continuar.");
            var user = await db.One("SELECT json_build_object('id',u.id,'role',u.role)::text FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=@hash AND s.expires_at>now()", ("hash", AuthModule.TokenHash(token)));
            if (user is null) throw new ApiException(401, "SESSION_EXPIRED", "La sesión expiró. Inicia sesión nuevamente.");
            actor = new Actor(user.Value.GetProperty("id").GetGuid(), user.Value.GetProperty("role").GetString()!);
            if (actor.Role == "SOLDADO_INACTIVE" && request.Action is not ("auth.me" or "auth.logout")) throw ApiException.Forbidden();
        }
        return await handler.Handle(request, actor, db, ct);
    }
}
