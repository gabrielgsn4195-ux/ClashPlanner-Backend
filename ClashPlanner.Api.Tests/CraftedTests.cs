using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClashPlanner.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests de integración del ajuste de defensas artesanales (`/crafted`): lectura
/// para cualquier autenticado, escritura solo para staff (Admin/Técnico).
/// </summary>
public class CraftedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
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

        var login = await f.CreateClient().PostAsJsonAsync("/auth/login", new { email, password = pwd });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        var client = f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Get_crafted_requiere_autenticacion()
    {
        var res = await factory.CreateClient().GetAsync("/crafted");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Get_crafted_sin_config_devuelve_modo_automatico()
    {
        using var f = new ApiFactory();
        var client = await AuthAsync(f);
        var res = await client.GetAsync("/crafted");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("activeGroups").ValueKind);
    }

    [Fact]
    public async Task Put_crafted_como_usuario_devuelve_403()
    {
        var client = await AuthAsync(factory); // rol por defecto: Usuario
        var res = await client.PutAsJsonAsync("/crafted", new { activeGroups = new[] { 103000008 } });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Put_como_tecnico_y_get_hacen_round_trip()
    {
        using var f = new ApiFactory();
        var staff = await AuthAsync(f, Roles.Tecnico);
        var put = await staff.PutAsJsonAsync("/crafted", new { activeGroups = new[] { 103000008, 103000010 } });
        put.EnsureSuccessStatusCode();

        var body = await (await staff.GetAsync("/crafted")).Content.ReadFromJsonAsync<JsonElement>();
        var groups = body.GetProperty("activeGroups").EnumerateArray().Select(g => g.GetInt32()).ToList();
        Assert.Equal(new[] { 103000008, 103000010 }, groups);

        // Volver al modo automático (null) también sobrevive el round-trip.
        (await staff.PutAsJsonAsync("/crafted", new { activeGroups = (int[]?)null })).EnsureSuccessStatusCode();
        body = await (await staff.GetAsync("/crafted")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("activeGroups").ValueKind);
    }

    [Fact]
    public async Task Put_crafted_invalido_devuelve_400()
    {
        using var f = new ApiFactory();
        var staff = await AuthAsync(f, Roles.Tecnico);
        // Id negativo.
        var res = await staff.PutAsJsonAsync("/crafted", new { activeGroups = new[] { -1 } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        // Demasiados grupos.
        res = await staff.PutAsJsonAsync("/crafted", new { activeGroups = Enumerable.Range(0, 21).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
