using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Hosting;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests del endurecimiento de seguridad de la Oleada 1: anti-enumeración en el
/// registro, bloqueo de cuenta y rate limit en /auth, y tope de tamaño del push de
/// sincronización.
/// </summary>
public class AuthSecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Pwd = "Passw0rd!23";

    private static async Task<HttpClient> RegisterAndAuthAsync(ApiFactory f)
    {
        var client = f.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/auth/register", new { email, password = Pwd });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Registro_de_email_duplicado_no_revela_la_cuenta()
    {
        var client = factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";

        (await client.PostAsJsonAsync("/auth/register", new { email, password = Pwd })).EnsureSuccessStatusCode();

        // Segundo registro con el mismo email: respuesta genérica 400 que NO revela que
        // el email ya está registrado (ni el propio email).
        var dup = await client.PostAsJsonAsync("/auth/register", new { email, password = Pwd });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var body = await dup.Content.ReadAsStringAsync();
        Assert.DoesNotContain(email, body);
        Assert.DoesNotContain("Duplicate", body);
    }

    [Fact]
    public async Task Login_se_bloquea_tras_varios_intentos_fallidos()
    {
        // Factory con un umbral de lockout bajo y explícito (evita acoplar el test al
        // default de producción con un número mágico).
        using var f = new TightLockoutFactory();
        var client = f.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/auth/register", new { email, password = Pwd })).EnsureSuccessStatusCode();

        // N intentos con contraseña incorrecta → la cuenta queda bloqueada.
        for (var i = 0; i < TightLockoutFactory.MaxAttempts; i++)
        {
            var bad = await client.PostAsJsonAsync("/auth/login", new { email, password = "incorrecta-000" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        }

        // Aun con la contraseña CORRECTA, sigue bloqueada (401) durante la ventana de lockout
        // (distingue lockout de simple contraseña incorrecta).
        var locked = await client.PostAsJsonAsync("/auth/login", new { email, password = Pwd });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task Push_con_demasiadas_cuentas_devuelve_413()
    {
        var client = await RegisterAndAuthAsync(factory);
        var accounts = Enumerable.Range(0, SyncLimits.MaxAccounts + 1).Select(i => new
        {
            id = $"acc-{i}",
            name = $"A{i}",
            color = "#fff",
            thLevel = 1,
            builders = 1,
            bhLevel = 0,
            bbBuilders = 0,
            goldPass = 0,
            modifiedAt = 1L
        }).ToArray();
        var data = new
        {
            accounts,
            jobs = Array.Empty<object>(),
            boosts = Array.Empty<object>(),
            helperStates = Array.Empty<object>(),
            helperLevels = new { },
            inventory = new { },
            plans = new { },
            overrides = new { }
        };

        var res = await client.PostAsJsonAsync("/sync", new { baseRevision = 0, data });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode); // 413
    }

    [Fact]
    public async Task Auth_aplica_rate_limit_por_ip()
    {
        // Fábrica con un límite bajo (3/min). En el resto de la suite el límite está
        // neutralizado en la factory base (RateLimit:AuthPerMinute alto), así que ningún
        // otro test puede toparse con un 429 ajeno.
        using var f = new TightAuthLimitFactory();
        var client = f.CreateClient();

        // Las 3 primeras peticiones a /auth se admiten (401 por credenciales falsas) y la
        // 4.ª, dentro de la misma ventana, se rechaza con 429: fija el límite en 3.
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var res = await client.PostAsJsonAsync("/auth/login", new { email = "x@example.com", password = "loquesea123" });
            codes.Add(res.StatusCode);
        }
        Assert.All(codes.Take(3), c => Assert.Equal(HttpStatusCode.Unauthorized, c));
        Assert.Equal(HttpStatusCode.TooManyRequests, codes[3]);
    }

    /// <summary>
    /// Fábrica que fija un límite de tasa de /auth bajo para probar el 429. Se aplica vía
    /// <c>UseSetting</c> (config de host), visible en tiempo de construcción —que es cuando
    /// el rate limiter lee el límite—, a diferencia de <c>ConfigureAppConfiguration</c>, que
    /// se aplica más tarde. Se llama DESPUÉS de base (que pone un valor alto): UseSetting es
    /// último-gana, así que el 3 manda.
    /// </summary>
    private sealed class TightAuthLimitFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimit:AuthPerMinute", "3");
        }
    }

    /// <summary>Fábrica que baja el umbral de lockout de Identity para probar el bloqueo.</summary>
    private sealed class TightLockoutFactory : ApiFactory
    {
        public const int MaxAttempts = 3;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Identity:MaxFailedAccessAttempts", MaxAttempts.ToString());
        }
    }
}
