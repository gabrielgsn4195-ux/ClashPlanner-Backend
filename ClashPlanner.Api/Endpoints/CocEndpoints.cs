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

        // Proxy de jugador y de clan (todo con el token de servidor y el mismo rate limit).
        g.MapGet("/player", (string tag, CocService coc) => Forward(tag, coc.GetPlayerAsync));
        g.MapGet("/clan", (string tag, CocService coc) => Forward(tag, coc.GetClanAsync));
        g.MapGet("/clan/currentwar", (string tag, CocService coc) => Forward(tag, coc.GetCurrentWarAsync));
        g.MapGet("/clan/warlog", (string tag, CocService coc) => Forward(tag, coc.GetWarLogAsync));
        g.MapGet("/clan/capitalraids", (string tag, CocService coc) => Forward(tag, coc.GetCapitalRaidsAsync));
        // Liga de Guerras de Clanes (CWL): grupo por etiqueta de clan, guerra por etiqueta de guerra.
        g.MapGet("/clan/leaguegroup", (string tag, CocService coc) => Forward(tag, coc.GetLeagueGroupAsync));
        g.MapGet("/clanwar", (string warTag, CocService coc) => Forward(warTag, coc.GetCwlWarAsync));
    }

    /// <summary>Llama al proxy con la etiqueta y reenvía cuerpo y estado tal cual.</summary>
    private static async Task<IResult> Forward(string tag, Func<string, Task<CocResult>> fetch)
    {
        if (string.IsNullOrWhiteSpace(tag)) return Results.BadRequest(new { reason = "missing-tag" });
        var res = await fetch(tag);
        return Results.Content(res.Json ?? "null", "application/json", statusCode: res.Status == 0 ? 502 : res.Status);
    }
}
