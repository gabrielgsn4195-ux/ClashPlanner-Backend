using System.Text.Json;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;

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

    private static readonly HashSet<string> ValidTargets = new(StringComparer.Ordinal) { "time", "cost" };

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

        // Editar: Admin o Técnico. Valida la forma y reescribe la lista completa.
        g.MapPut("", async (List<GameEventDto> events, AppSettingsService settings) =>
        {
            foreach (var e in events)
                foreach (var eff in e.Effects)
                    if (!ValidTargets.Contains(eff.Target))
                        return Results.BadRequest(new { reason = "invalid-target", target = eff.Target });

            var json = JsonSerializer.Serialize(events, Json);
            await settings.SetAsync(SettingKeys.EventsConfig, json);
            return Results.Ok(new { ok = true, count = events.Count });
        })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Tecnico));
    }
}
