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
}
