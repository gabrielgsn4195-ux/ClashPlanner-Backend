using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClashPlanner.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests de integración de los eventos globales (`/events`): lectura para
/// cualquier autenticado, escritura solo para staff (Admin/Técnico).
/// </summary>
public class EventsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>
    /// Registra un usuario (opcionalmente con un rol) y devuelve un cliente
    /// autenticado con un token que ya incluye ese rol.
    /// </summary>
    private static async Task<HttpClient> AuthAsync(ApiFactory f, string? role = null)
    {
        var email = $"u{Guid.NewGuid():N}@example.com";
        const string pwd = "Passw0rd!23";
        (await f.CreateClient().PostAsJsonAsync("/auth/register", new { email, password = pwd }))
            .EnsureSuccessStatusCode();

        if (role is not null)
        {
            using var scope = f.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            await users.AddToRoleAsync(user!, role);
        }

        // (Re)login para obtener un token con los roles actuales.
        var login = await f.CreateClient().PostAsJsonAsync("/auth/login", new { email, password = pwd });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Un evento de ejemplo con ventana, efecto filtrado y rótulo.</summary>
    private static object[] SampleEvents() => new object[]
    {
        new
        {
            id = "e1",
            name = "Constructor Duende",
            enabled = true,
            startsAt = 1000L,
            endsAt = 2000L,
            goblinBuilder = true,
            effects = new[] { new { target = "cost", percent = 50.0, categories = new[] { "defense", "wall" } } },
            banner = new { show = true, message = "Cuidado con tus gemas 💎" }
        }
    };

    [Fact]
    public async Task Get_events_requiere_autenticacion()
    {
        var res = await factory.CreateClient().GetAsync("/events");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Get_events_sin_config_devuelve_lista_vacia()
    {
        // Fábrica propia (BD limpia): la config de eventos es GLOBAL y otros tests
        // la pueblan, así que el caso «sin config» necesita estado aislado.
        using var f = new ApiFactory();
        var client = await AuthAsync(f);
        var res = await client.GetAsync("/events");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task Put_events_como_usuario_devuelve_403()
    {
        var client = await AuthAsync(factory); // rol por defecto: Usuario
        var res = await client.PutAsJsonAsync("/events", SampleEvents());
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Put_como_tecnico_y_get_hacen_round_trip()
    {
        using var f = new ApiFactory();
        var staff = await AuthAsync(f, Roles.Tecnico);
        var put = await staff.PutAsJsonAsync("/events", SampleEvents());
        put.EnsureSuccessStatusCode();

        var body = await (await staff.GetAsync("/events")).Content.ReadFromJsonAsync<JsonElement>();
        var arr = body.EnumerateArray().ToList();
        Assert.Single(arr);
        var e = arr[0];
        Assert.Equal("Constructor Duende", e.GetProperty("name").GetString());
        Assert.Equal(1000L, e.GetProperty("startsAt").GetInt64());
        Assert.Equal(2000L, e.GetProperty("endsAt").GetInt64());
        Assert.True(e.GetProperty("goblinBuilder").GetBoolean());
        var cats = e.GetProperty("effects")[0].GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal(new[] { "defense", "wall" }, cats);
        Assert.Equal(50.0, e.GetProperty("effects")[0].GetProperty("percent").GetDouble());
        Assert.True(e.GetProperty("banner").GetProperty("show").GetBoolean());
        Assert.Equal("Cuidado con tus gemas 💎", e.GetProperty("banner").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Put_events_con_effects_null_no_revienta_y_se_guarda()
    {
        using var f = new ApiFactory();
        var staff = await AuthAsync(f, Roles.Tecnico);
        // "effects": null en el JSON → antes provocaba NullReferenceException (500). F-009.
        var events = new object[]
        {
            new { id = "e1", name = "Sin efectos", enabled = true, goblinBuilder = false, effects = (object?)null }
        };
        var put = await staff.PutAsJsonAsync("/events", events);
        put.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Put_events_con_demasiados_eventos_devuelve_400()
    {
        using var f = new ApiFactory();
        var staff = await AuthAsync(f, Roles.Tecnico);
        var events = Enumerable.Range(0, 201)
            .Select(i => new { id = $"e{i}", name = "x", enabled = true, goblinBuilder = false, effects = Array.Empty<object>() })
            .ToArray();
        var put = await staff.PutAsJsonAsync("/events", events);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_events_sanea_el_html_del_rotulo_en_el_servidor()
    {
        using var f = new ApiFactory();
        var staff = await AuthAsync(f, Roles.Tecnico);
        var events = new object[]
        {
            new
            {
                id = "e1", name = "X", enabled = true, goblinBuilder = false,
                effects = Array.Empty<object>(),
                banner = new { show = true, message = "<b>hola</b><script>alert(1)</script><img src=x onerror=alert(1)>" }
            }
        };
        (await staff.PutAsJsonAsync("/events", events)).EnsureSuccessStatusCode();

        var body = await (await staff.GetAsync("/events")).Content.ReadFromJsonAsync<JsonElement>();
        var msg = body.EnumerateArray().First().GetProperty("banner").GetProperty("message").GetString()!;
        // El formato permitido se conserva; el script, el handler y la etiqueta no permitida se eliminan. F-011.
        Assert.Contains("hola", msg);
        Assert.DoesNotContain("script", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", msg, StringComparison.OrdinalIgnoreCase);
    }
}
