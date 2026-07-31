using System.Text.Json;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;

namespace ClashPlanner.Api.Endpoints;

/// <summary>
/// Endpoints del ajuste de defensas artesanales activas (bajo <c>/crafted</c>):
///  - <b>Leer</b> (GET): cualquier usuario autenticado (todos los clientes deben
///    gatear la planificación con el mismo ajuste).
///  - <b>Editar</b> (PUT): Admin o Técnico.
///
/// El ajuste se guarda como JSON (camelCase) en la tabla `Settings` bajo
/// <see cref="SettingKeys.CraftedConfig"/>. `activeGroups: null` = automático
/// (decide el flag del catálogo del cliente); con lista, solo esos grupos son
/// planificables. El servidor solo valida y persiste (patrón de /events).
/// </summary>
public static class CraftedEndpoints
{
    /// <summary>Serialización camelCase, coherente con el cliente TS.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Tope holgado: fases de 3 defensas; nunca deberían acumularse más grupos.
    private const int MaxGroups = 20;

    public static void MapCraftedEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/crafted").WithTags("Crafted");

        // Leer: cualquier usuario autenticado. Sin ajuste guardado → automático.
        g.MapGet("", async (AppSettingsService settings) =>
        {
            var json = await settings.GetStringAsync(SettingKeys.CraftedConfig);
            return Results.Content(
                string.IsNullOrWhiteSpace(json) ? """{"activeGroups":null}""" : json,
                "application/json");
        })
            .RequireAuthorization();

        // Editar: Admin o Técnico. Valida forma/tamaño y reescribe el ajuste.
        g.MapPut("", async (CraftedConfigDto config, AppSettingsService settings) =>
        {
            if (config.ActiveGroups is { } groups &&
                (groups.Count > MaxGroups || groups.Any(id => id < 0)))
                return Results.BadRequest(new { reason = "invalid-crafted-config" });

            var json = JsonSerializer.Serialize(config, Json);
            await settings.SetAsync(SettingKeys.CraftedConfig, json);
            return Results.Ok(new { ok = true });
        })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Tecnico));
    }
}
