using System.Net;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests del proxy de Clash of Clans (nuevo modelo): un único token de servidor,
/// endpoint público (sin sesión) y limitado por tasa. El usuario nunca gestiona
/// tokens. La llamada real a la API de CoC no se prueba (requiere red y token);
/// aquí se verifica el comportamiento sin token de servidor configurado.
/// </summary>
public class CocTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task El_proxy_no_requiere_sesion()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/coc/player?tag=%23ABC123");
        // No exige autenticación (no devuelve 401); el acceso es público.
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Sin_token_de_servidor_configurado_responde_502()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/coc/player?tag=%23ABC123");
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("server-token-not-configured", body);
    }

    [Fact]
    public async Task Consultar_jugador_sin_tag_devuelve_400()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/coc/player?tag=");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("/coc/clan")]
    [InlineData("/coc/clan/currentwar")]
    [InlineData("/coc/clan/warlog")]
    [InlineData("/coc/clan/capitalraids")]
    public async Task Los_endpoints_de_clan_son_publicos_y_sin_token_responden_502(string path)
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync($"{path}?tag=%23ABC123");
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Contains("server-token-not-configured", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Consultar_clan_sin_tag_devuelve_400()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/coc/clan?tag=");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("/coc/player?tag=bad!tag")] // carácter no alfanumérico
    [InlineData("/coc/player?tag=%23..%2Fetc")] // intento de path traversal
    [InlineData("/coc/clan?tag=%23AB")] // demasiado corta (<3)
    public async Task Tag_con_formato_invalido_devuelve_400(string url)
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("invalid-tag", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Endpoints_de_cwl_publicos_y_sin_token_responden_502()
    {
        var client = factory.CreateClient();
        var group = await client.GetAsync("/coc/clan/leaguegroup?tag=%23ABC123");
        Assert.Equal(HttpStatusCode.BadGateway, group.StatusCode);
        var war = await client.GetAsync("/coc/clanwar?warTag=%23WAR123");
        Assert.Equal(HttpStatusCode.BadGateway, war.StatusCode);
    }

    [Fact]
    public async Task Consultar_cwl_war_sin_warTag_devuelve_400()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/coc/clanwar?warTag=");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
