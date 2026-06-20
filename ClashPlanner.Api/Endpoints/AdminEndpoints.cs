using ClashPlanner.Api.Data;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClashPlanner.Api.Endpoints;

/// <summary>
/// Endpoints de administración (bajo <c>/admin</c>), protegidos por rol:
///  - Configuración general (tabla Settings): <b>leer</b> Admin o Técnico;
///    <b>editar</b> solo Admin.
///  - Usuarios y roles: solo Admin.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Claves editables permitidas (whitelist para no escribir claves arbitrarias).</summary>
    private static readonly HashSet<string> Editable = new(StringComparer.Ordinal)
    {
        SettingKeys.CocToken, SettingKeys.CocUseProxy, SettingKeys.CocProxyUrl,
        SettingKeys.CocDirectUrl, SettingKeys.CocTimeoutSeconds,
        SettingKeys.RateLimitCocPerMinute, SettingKeys.CorsOrigins
    };

    /// <summary>Ajustes que solo se aplican al reiniciar el servidor (aviso al cliente).</summary>
    private static readonly HashSet<string> RestartRequired = new(StringComparer.Ordinal)
    {
        SettingKeys.RateLimitCocPerMinute, SettingKeys.CorsOrigins
    };

    public record SettingUpdate(string Key, string Value);
    public record RolesUpdate(string[] Roles);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").WithTags("Admin");

        // ── Configuración general ────────────────────────────────────────────
        // Leer: Admin o Técnico (Técnico en solo lectura → no expone el PUT).
        g.MapGet("/settings", async (AppSettingsService settings) =>
            Results.Ok(new
            {
                settings = await settings.ListMaskedAsync(),
                restartRequired = RestartRequired
            }))
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Tecnico));

        // Editar: solo Admin.
        g.MapPut("/settings", async (SettingUpdate body, AppSettingsService settings) =>
        {
            if (!Editable.Contains(body.Key))
                return Results.BadRequest(new { reason = "unknown-key", key = body.Key });
            // Las URLs base del proxy CoC deben ser HTTPS absolutas: como la petición del
            // proxy lleva el token de servidor en la cabecera Authorization, una URL hacia
            // una IP interna o un esquema peligroso sería un vector de SSRF / fuga del token.
            if ((body.Key == SettingKeys.CocProxyUrl || body.Key == SettingKeys.CocDirectUrl)
                && !string.IsNullOrWhiteSpace(body.Value)
                && !(Uri.TryCreate(body.Value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps))
                return Results.BadRequest(new { reason = "invalid-url", key = body.Key });
            await settings.SetAsync(body.Key, body.Value ?? string.Empty);
            return Results.Ok(new
            {
                ok = true,
                restartRequired = RestartRequired.Contains(body.Key)
            });
        })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        // ── Usuarios y roles (solo Admin) ────────────────────────────────────
        g.MapGet("/users", async (UserManager<ApplicationUser> users, AppDbContext db) =>
        {
            var list = await users.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
            // Roles de TODOS los usuarios en una sola consulta (join UserRoles × Roles),
            // en vez de un GetRolesAsync() por usuario (N+1: 1 + N consultas).
            var roles = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               where r.Name != null
                               select new { ur.UserId, Role = r.Name! }).ToListAsync();
            var rolesByUser = roles.GroupBy(x => x.UserId)
                .ToDictionary(grp => grp.Key, grp => grp.Select(x => x.Role).ToArray());
            var result = list.Select(u => new
            {
                id = u.Id,
                email = u.Email,
                roles = rolesByUser.TryGetValue(u.Id, out var rs) ? rs : Array.Empty<string>()
            });
            return Results.Ok(result);
        })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));

        g.MapPut("/users/{id}/roles", async (
            string id, RolesUpdate body, UserManager<ApplicationUser> users) =>
        {
            var valid = body.Roles.Where(Roles.All.Contains).Distinct().ToArray();
            var user = await users.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            var current = await users.GetRolesAsync(user);
            await users.RemoveFromRolesAsync(user, current.Except(valid));
            await users.AddToRolesAsync(user, valid.Except(current));
            return Results.Ok(new { id, roles = valid });
        })
            .RequireAuthorization(p => p.RequireRole(Roles.Admin));
    }
}
