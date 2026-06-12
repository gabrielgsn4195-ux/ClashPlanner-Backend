using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests de integración del proxy de Clash of Clans: autorización, guardado/
/// borrado del token (cifrado en reposo) y comportamiento sin token. La llamada
/// real a la API de CoC no se prueba aquí (requiere red y un token válido).
/// </summary>
public class CocTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> AuthAsync()
    {
        var client = factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/auth/register", new { email, password = "Passw0rd!23" });
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task El_proxy_requiere_autenticacion()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/coc/token");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Guardar_token_se_refleja_en_el_estado()
    {
        var client = await AuthAsync();

        var before = await (await client.GetAsync("/coc/token")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(before.GetProperty("hasToken").GetBoolean());

        var put = await client.PutAsJsonAsync("/coc/token", new { token = "mi-token-secreto" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var after = await (await client.GetAsync("/coc/token")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(after.GetProperty("hasToken").GetBoolean());
    }

    [Fact]
    public async Task Borrar_token_lo_elimina()
    {
        var client = await AuthAsync();
        await client.PutAsJsonAsync("/coc/token", new { token = "abc" });
        var del = await client.DeleteAsync("/coc/token");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var status = await (await client.GetAsync("/coc/token")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(status.GetProperty("hasToken").GetBoolean());
    }

    [Fact]
    public async Task Guardar_token_vacio_devuelve_400()
    {
        var client = await AuthAsync();
        var res = await client.PutAsJsonAsync("/coc/token", new { token = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Consultar_jugador_sin_token_responde_502_no_token()
    {
        var client = await AuthAsync();
        var res = await client.GetAsync("/coc/player?tag=%23ABC123");
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("no-token", body);
    }

    [Fact]
    public async Task Consultar_jugador_sin_tag_devuelve_400()
    {
        var client = await AuthAsync();
        var res = await client.GetAsync("/coc/player?tag=");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
