using System.Net;
using System.Net.Http.Json;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests del endurecimiento del pipeline (Oleada 1): cabeceras de seguridad presentes
/// en todas las respuestas y errores en formato ProblemDetails.
/// </summary>
public class SecurityHeadersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Las_respuestas_llevan_cabeceras_de_seguridad()
    {
        var res = await factory.CreateClient().GetAsync("/health");
        res.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task El_error_de_registro_duplicado_usa_ProblemDetails()
    {
        var client = factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        const string pwd = "Passw0rd!23";
        (await client.PostAsJsonAsync("/auth/register", new { email, password = pwd })).EnsureSuccessStatusCode();

        var dup = await client.PostAsJsonAsync("/auth/register", new { email, password = pwd });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Equal("application/problem+json", dup.Content.Headers.ContentType?.MediaType);
        // Cuerpo neutro: ni el email ni el código de Identity; pero sí el reason estable.
        var body = await dup.Content.ReadAsStringAsync();
        Assert.Contains("register-failed", body);
        Assert.DoesNotContain(email, body);
    }
}
