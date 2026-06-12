using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClashPlanner.Api.Services;

namespace ClashPlanner.Api.Endpoints;

/// <summary>Cuerpo para guardar el token de CoC.</summary>
public class SetCocTokenRequest
{
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Endpoints del proxy de Clash of Clans (requieren sesión): guardar/borrar el
/// token de CoC del usuario y consultar un jugador a través del servidor (con la
/// IP fija del backend autorizada en el token).
/// </summary>
public static class CocEndpoints
{
    public static void MapCocEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/coc").WithTags("CoC").RequireAuthorization();

        // Estado del token + IP del servidor a autorizar al crearlo.
        g.MapGet("/token", async (ClaimsPrincipal user, CocService coc) =>
        {
            var userId = UserId(user);
            if (userId is null) return Results.Unauthorized();
            return Results.Ok(new { hasToken = await coc.HasTokenAsync(userId), serverIp = await coc.GetServerIpAsync() });
        });

        // Guarda el token (cifrado en reposo).
        g.MapPut("/token", async (SetCocTokenRequest req, ClaimsPrincipal user, CocService coc) =>
        {
            var userId = UserId(user);
            if (userId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Token)) return Results.BadRequest(new { reason = "empty-token" });
            await coc.SetTokenAsync(userId, req.Token.Trim());
            return Results.NoContent();
        });

        // Borra el token.
        g.MapDelete("/token", async (ClaimsPrincipal user, CocService coc) =>
        {
            var userId = UserId(user);
            if (userId is null) return Results.Unauthorized();
            await coc.ClearTokenAsync(userId);
            return Results.NoContent();
        });

        // Proxy de jugador: GET /coc/player?tag=%23ABC
        g.MapGet("/player", async (string tag, ClaimsPrincipal user, CocService coc) =>
        {
            var userId = UserId(user);
            if (userId is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest(new { reason = "missing-tag" });
            var res = await coc.GetPlayerAsync(userId, tag);
            // Reenvía el cuerpo y el estado tal cual los devolvió la API de CoC.
            return Results.Content(res.Json ?? "null", "application/json", statusCode: res.Status == 0 ? 502 : res.Status);
        });
    }

    private static string? UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
