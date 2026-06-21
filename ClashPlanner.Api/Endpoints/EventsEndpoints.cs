using System.Text.Json;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Ganss.Xss;

namespace ClashPlanner.Api.Endpoints;

/// <summary>
/// Endpoints de los eventos GLOBALES del juego (bajo <c>/events</c>):
///  - <b>Leer</b> (GET): cualquier usuario autenticado, para que todos los clientes
///    apliquen los mismos eventos.
///  - <b>Editar</b> (PUT): Admin o Técnico.
///
/// La lista se guarda como JSON (camelCase) en la tabla `Settings` bajo la clave
/// <see cref="SettingKeys.EventsConfig"/>. El servidor valida la forma y persiste;
/// no calcula efectos ni evalúa las ventanas temporales (eso lo hace el cliente).
/// </summary>
public static class EventsEndpoints
{
    /// <summary>Serialización camelCase, coherente con el cliente TS.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ValidTargets = new(StringComparer.Ordinal) { "time", "cost", "both" };

    // Topes para no inflar la fila Settings de eventos (se cachea y se sirve a TODOS). F-009.
    private const int MaxEvents = 200;
    private const int MaxEffects = 50;
    private const int MaxTextLength = 4_000;

    /// <summary>
    /// Saneador del HTML del rótulo (defensa en profundidad: el cliente también lo sanea).
    /// Lista blanca alineada con EventsBanner del cliente: solo formato en línea y los
    /// estilos color/background-color, sin URLs. Ver auditoría F-011.
    /// </summary>
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[] { "b", "strong", "i", "em", "u", "s", "span", "br" }) s.AllowedTags.Add(t);
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("style");
        s.AllowedCssProperties.Clear();
        s.AllowedCssProperties.Add("color");
        s.AllowedCssProperties.Add("background-color");
        s.AllowedSchemes.Clear();
        return s;
    }

    public static void MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/events").WithTags("Events");

        // Leer: cualquier usuario autenticado. Devuelve el JSON guardado tal cual
        // (o `[]` si aún no hay eventos).
        g.MapGet("", async (AppSettingsService settings) =>
        {
            var json = await settings.GetStringAsync(SettingKeys.EventsConfig);
            return Results.Content(
                string.IsNullOrWhiteSpace(json) ? "[]" : json,
                "application/json");
        })
            .RequireAuthorization();

        // Editar: Admin o Técnico. Valida forma/tamaño, sanea el HTML del rótulo y
        // reescribe la lista completa.
        g.MapPut("", async (List<GameEventDto> events, AppSettingsService settings) =>
        {
            if (events.Count > MaxEvents)
                return Results.BadRequest(new { reason = "too-many-events", count = events.Count });

            foreach (var e in events)
            {
                // `effects` puede llegar null en el JSON → trátalo como lista vacía (evita
                // NullReferenceException → 500). Ver auditoría F-009.
                var effects = e.Effects ?? [];
                if (effects.Count > MaxEffects)
                    return Results.BadRequest(new { reason = "too-many-effects" });
                if ((e.Name?.Length ?? 0) > MaxTextLength || (e.Banner?.Message?.Length ?? 0) > MaxTextLength)
                    return Results.BadRequest(new { reason = "text-too-long" });
                foreach (var eff in effects)
                    if (!ValidTargets.Contains(eff.Target))
                        return Results.BadRequest(new { reason = "invalid-target", target = eff.Target });
                e.Effects = effects;
                // Saneo en el SERVIDOR del HTML del rótulo (se reparte a TODOS los usuarios):
                // defensa en profundidad además del saneo del cliente. Ver auditoría F-011.
                if (e.Banner is not null)
                    e.Banner.Message = Sanitizer.Sanitize(e.Banner.Message);
            }

            var json = JsonSerializer.Serialize(events, Json);
            await settings.SetAsync(SettingKeys.EventsConfig, json);
            return Results.Ok(new { ok = true, count = events.Count });
        })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Tecnico));
    }
}
