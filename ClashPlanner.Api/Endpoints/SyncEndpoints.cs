using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClashPlanner.Api.Endpoints;

/// <summary>
/// Endpoints de sincronización (requieren sesión): descargar (pull) y subir
/// (push) el snapshot del planificador del usuario autenticado.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/sync").WithTags("Sync").RequireAuthorization();

        // Gate de versión mínima: si `Sync:MinClientVersion` está fijado y el cliente
        // (cabecera `X-Client-Version`) es más viejo, devolvemos 426 para que pause el
        // sync y pida actualizar. Si la cabecera falta o no se puede interpretar →
        // fail-open (clientes antiguos que no la mandan siguen sincronizando). Solo se
        // gatea /sync: es donde un cliente viejo podría pisar datos nuevos de otros
        // dispositivos (la app sigue usable en local).
        g.AddEndpointFilter(static async (ctx, next) =>
        {
            var cfg = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var min = cfg["Sync:MinClientVersion"];
            if (!string.IsNullOrWhiteSpace(min) && Version.TryParse(min, out var minV))
            {
                var header = ctx.HttpContext.Request.Headers["X-Client-Version"].ToString();
                if (Version.TryParse(header, out var clientV) && clientV < minV)
                    return Results.Json(new { minVersion = min }, statusCode: StatusCodes.Status426UpgradeRequired);
            }
            return await next(ctx);
        });

        // Pull: snapshot completo + revisión actual.
        g.MapGet("", async (ClaimsPrincipal user, SyncService sync) =>
        {
            var userId = UserId(user);
            return userId is null ? Results.Unauthorized() : Results.Ok(await sync.PullAsync(userId));
        });

        // Push: aplica el snapshot si la revisión base coincide; si no, 409 con
        // el estado del servidor para que el cliente fusione y reintente.
        g.MapPost("", async (PushRequest req, ClaimsPrincipal user, SyncService sync) =>
        {
            var userId = UserId(user);
            if (userId is null) return Results.Unauthorized();
            // Tope de cardinalidad del snapshot (DoS por usuario autenticado): el tope de
            // tamaño de cuerpo lo pone Kestrel; aquí limitamos el nº de entidades y la
            // longitud de los campos de texto antes de tocar la BD.
            if (SyncLimits.Validate(req.Data) is { } tooLarge)
                return Results.Json(new { reason = "payload-too-large", detail = tooLarge },
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            var res = await sync.PushAsync(userId, req);
            return res.Conflict ? Results.Json(res, statusCode: StatusCodes.Status409Conflict) : Results.Ok(res);
        });
    }

    /// <summary>Id del usuario autenticado a partir del claim `sub` del JWT.</summary>
    private static string? UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
