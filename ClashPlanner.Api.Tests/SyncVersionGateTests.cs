using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClashPlanner.Api.Tests;

/// <summary>Fábrica con un mínimo de versión de cliente fijado: el gate de /sync está activo.</summary>
public class GatedApiFactory : ApiFactory
{
    public const string MinVersion = "0.2.0";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Sync:MinClientVersion", MinVersion);
        base.ConfigureWebHost(builder);
    }
}

/// <summary>
/// Tests del gate de versión mínima en <c>/sync</c>: un cliente más viejo que
/// <c>Sync:MinClientVersion</c> recibe 426; los demás pasan (incluido fail-open sin
/// cabecera y con el mínimo sin configurar).
/// </summary>
public class SyncVersionGateTests(GatedApiFactory gated, ApiFactory plain)
    : IClassFixture<GatedApiFactory>, IClassFixture<ApiFactory>
{
    /// <summary>Registra un usuario, autentica y (opcional) fija la cabecera de versión.</summary>
    private static async Task<HttpClient> AuthAsync(WebApplicationFactory<Program> f, string? clientVersion)
    {
        var client = f.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var reg = await client.PostAsJsonAsync("/auth/register", new { email, password = "Passw0rd!23" });
        reg.EnsureSuccessStatusCode();
        var auth = await reg.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        if (clientVersion is not null) client.DefaultRequestHeaders.Add("X-Client-Version", clientVersion);
        return client;
    }

    [Fact]
    public async Task Cliente_mas_viejo_que_el_minimo_recibe_426_con_minVersion()
    {
        var client = await AuthAsync(gated, "0.1.9");
        var res = await client.GetAsync("/sync");
        Assert.Equal(HttpStatusCode.UpgradeRequired, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GatedApiFactory.MinVersion, body.GetProperty("minVersion").GetString());
    }

    [Fact]
    public async Task Cliente_al_dia_pasa()
    {
        var client = await AuthAsync(gated, GatedApiFactory.MinVersion);
        var res = await client.GetAsync("/sync");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Sin_cabecera_de_version_pasa_fail_open()
    {
        var client = await AuthAsync(gated, clientVersion: null);
        var res = await client.GetAsync("/sync");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Sin_minimo_configurado_no_gatea()
    {
        var client = await AuthAsync(plain, "0.0.1");
        var res = await client.GetAsync("/sync");
        res.EnsureSuccessStatusCode();
    }
}
