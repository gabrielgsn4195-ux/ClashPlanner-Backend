using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests de los endpoints de administración (<c>/admin</c>): configuración general
/// (leer: Admin o Técnico; editar: solo Admin) y gestión de usuarios/roles (solo Admin).
/// Cubre tanto la autorización por rol como el comportamiento (enmascarado de secretos,
/// aviso de reinicio y reemplazo del conjunto de roles).
/// </summary>
public class AdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// Registra un usuario (opcionalmente con un rol), inicia sesión y devuelve un cliente
    /// autenticado con un token que ya incluye ese rol, junto con su email.
    /// </summary>
    private static async Task<(HttpClient Client, string Email)> AuthAsync(ApiFactory f, string? role = null)
    {
        var email = $"u{Guid.NewGuid():N}@example.com";
        const string pwd = "Passw0rd!23";
        (await f.CreateClient().PostAsJsonAsync("/auth/register", new { email, password = pwd }))
            .EnsureSuccessStatusCode();

        if (role is not null)
        {
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddToRoleAsync((await users.FindByEmailAsync(email))!, role);
        }

        var login = await f.CreateClient().PostAsJsonAsync("/auth/login", new { email, password = pwd });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, email);
    }

    private static List<string?> RolesOf(JsonElement user) =>
        user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();

    // ── Configuración (/admin/settings) ──────────────────────────────────────

    [Fact]
    public async Task Get_settings_sin_sesion_devuelve_401()
    {
        var res = await factory.CreateClient().GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Get_settings_como_usuario_devuelve_403()
    {
        var (client, _) = await AuthAsync(factory); // rol por defecto: Usuario
        var res = await client.GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Get_settings_como_tecnico_lee_la_configuracion()
    {
        var (client, _) = await AuthAsync(factory, Models.Roles.Tecnico);
        var res = await client.GetAsync("/admin/settings");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.GetProperty("settings").ValueKind);
        var restart = body.GetProperty("restartRequired").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(SettingKeys.RateLimitCocPerMinute, restart);
        Assert.Contains(SettingKeys.CorsOrigins, restart);
    }

    [Fact]
    public async Task Put_settings_como_tecnico_devuelve_403()
    {
        // Editar es solo Admin (Técnico es de solo lectura).
        var (client, _) = await AuthAsync(factory, Models.Roles.Tecnico);
        var res = await client.PutAsJsonAsync("/admin/settings", new { key = SettingKeys.CocUseProxy, value = "false" });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Put_settings_clave_desconocida_devuelve_400()
    {
        var (client, _) = await AuthAsync(factory, Models.Roles.Admin);
        var res = await client.PutAsJsonAsync("/admin/settings", new { key = "Clave:Arbitraria", value = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("unknown-key", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Put_settings_clave_de_reinicio_avisa_restartRequired()
    {
        using var f = new ApiFactory();
        var (admin, _) = await AuthAsync(f, Models.Roles.Admin);
        var res = await admin.PutAsJsonAsync("/admin/settings", new { key = SettingKeys.RateLimitCocPerMinute, value = "42" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("restartRequired").GetBoolean());
    }

    [Fact]
    public async Task Put_settings_round_trip_valor_no_secreto_visible()
    {
        using var f = new ApiFactory();
        var (admin, _) = await AuthAsync(f, Models.Roles.Admin);
        (await admin.PutAsJsonAsync("/admin/settings", new { key = SettingKeys.CocDirectUrl, value = "https://example.test/v1" }))
            .EnsureSuccessStatusCode();
        var body = await (await admin.GetAsync("/admin/settings")).Content.ReadFromJsonAsync<JsonElement>();
        var entry = body.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == SettingKeys.CocDirectUrl);
        Assert.Equal("https://example.test/v1", entry.GetProperty("value").GetString());
        Assert.False(entry.GetProperty("isSecret").GetBoolean());
    }

    [Fact]
    public async Task Put_settings_url_de_proxy_no_https_devuelve_400()
    {
        // Anti-SSRF: las URLs base del proxy CoC deben ser HTTPS absolutas (no se puede
        // apuntar el token de servidor a una IP interna ni a un esquema peligroso).
        using var f = new ApiFactory();
        var (admin, _) = await AuthAsync(f, Models.Roles.Admin);
        var res = await admin.PutAsJsonAsync("/admin/settings",
            new { key = SettingKeys.CocProxyUrl, value = "http://169.254.169.254/v1" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("invalid-url", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Put_settings_token_se_guarda_enmascarado_nunca_en_claro()
    {
        using var f = new ApiFactory();
        var (admin, _) = await AuthAsync(f, Models.Roles.Admin);
        (await admin.PutAsJsonAsync("/admin/settings", new { key = SettingKeys.CocToken, value = "super-secreto-123" }))
            .EnsureSuccessStatusCode();
        var body = await (await admin.GetAsync("/admin/settings")).Content.ReadFromJsonAsync<JsonElement>();
        var entry = body.GetProperty("settings").EnumerateArray()
            .First(s => s.GetProperty("key").GetString() == SettingKeys.CocToken);
        Assert.True(entry.GetProperty("isSecret").GetBoolean());
        Assert.True(entry.GetProperty("isSet").GetBoolean());
        Assert.Equal("********", entry.GetProperty("value").GetString());
        // El valor real nunca aparece en la respuesta.
        Assert.DoesNotContain("super-secreto", JsonSerializer.Serialize(body));
    }

    // ── Usuarios y roles (/admin/users) ──────────────────────────────────────

    [Fact]
    public async Task Get_users_como_usuario_devuelve_403()
    {
        var (client, _) = await AuthAsync(factory); // Usuario
        var res = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Get_users_como_admin_lista_con_roles()
    {
        using var f = new ApiFactory();
        var (admin, adminEmail) = await AuthAsync(f, Models.Roles.Admin);
        var body = await (await admin.GetAsync("/admin/users")).Content.ReadFromJsonAsync<JsonElement>();
        var users = body.EnumerateArray().ToList();
        Assert.NotEmpty(users);
        // El propio admin aparece con su rol (verifica también el mapeo de roles en lote).
        var me = users.First(u => u.GetProperty("email").GetString() == adminEmail);
        Assert.Contains(Models.Roles.Admin, RolesOf(me));
    }

    [Fact]
    public async Task Put_roles_como_usuario_devuelve_403()
    {
        var (client, _) = await AuthAsync(factory); // Usuario
        var res = await client.PutAsJsonAsync(
            $"/admin/users/{Guid.NewGuid():N}/roles", new { roles = new[] { Models.Roles.Tecnico } });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Put_roles_como_admin_reemplaza_el_conjunto_de_roles()
    {
        using var f = new ApiFactory();
        var (admin, _) = await AuthAsync(f, Models.Roles.Admin);
        var (_, targetEmail) = await AuthAsync(f); // usuario objetivo (rol Usuario)

        var list = await (await admin.GetAsync("/admin/users")).Content.ReadFromJsonAsync<JsonElement>();
        var targetId = list.EnumerateArray()
            .First(u => u.GetProperty("email").GetString() == targetEmail).GetProperty("id").GetString();

        (await admin.PutAsJsonAsync($"/admin/users/{targetId}/roles", new { roles = new[] { Models.Roles.Tecnico } }))
            .EnsureSuccessStatusCode();

        var after = await (await admin.GetAsync("/admin/users")).Content.ReadFromJsonAsync<JsonElement>();
        var target = after.EnumerateArray().First(u => u.GetProperty("id").GetString() == targetId);
        // PUT reemplaza el conjunto: queda con Técnico y pierde Usuario.
        Assert.Contains(Models.Roles.Tecnico, RolesOf(target));
        Assert.DoesNotContain(Models.Roles.Usuario, RolesOf(target));
    }
}
