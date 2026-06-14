using ClashPlanner.Api.Services;

namespace ClashPlanner.Api.Endpoints;

/// <summary>
/// Proxy público de Clash of Clans: consulta un jugador a través del servidor
/// (que usa un único token de servidor). No requiere sesión —el usuario nunca
/// gestiona tokens— pero está limitado por tasa para no agotar la cuota de la API.
/// </summary>
public static class CocEndpoints
{
    public static void MapCocEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/coc").WithTags("CoC").RequireRateLimiting("coc");

        // Proxy de jugador: GET /coc/player?tag=%23ABC
        g.MapGet("/player", async (string tag, CocService coc) =>
        {
            if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest(new { reason = "missing-tag" });
            var res = await coc.GetPlayerAsync(tag);
            // Reenvía el cuerpo y el estado tal cual los devolvió la API de CoC.
            return Results.Content(res.Json ?? "null", "application/json", statusCode: res.Status == 0 ? 502 : res.Status);
        });
    }
}
