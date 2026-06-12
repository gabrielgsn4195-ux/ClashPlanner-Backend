using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Services;

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
            var res = await sync.PushAsync(userId, req);
            return res.Conflict ? Results.Json(res, statusCode: StatusCodes.Status409Conflict) : Results.Ok(res);
        });
    }

    /// <summary>Id del usuario autenticado a partir del claim `sub` del JWT.</summary>
    private static string? UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
