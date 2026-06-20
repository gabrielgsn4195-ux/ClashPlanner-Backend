using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClashPlanner.Api.Data;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests del endurecimiento de los refresh tokens (Oleada 2): rotación con familia,
/// detección de reuso (revocación en cascada), logout / logout-all, deadline absoluto
/// y purga de tokens muertos.
/// </summary>
public class RefreshTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Pwd = "Passw0rd!23";

    private static async Task<JsonElement> RegisterAsync(ApiFactory f)
    {
        var email = $"u{Guid.NewGuid():N}@example.com";
        var res = await f.CreateClient().PostAsJsonAsync("/auth/register", new { email, password = Pwd });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Logout_revoca_el_refresh_token()
    {
        var auth = await RegisterAsync(factory);
        var refresh = auth.GetProperty("refreshToken").GetString();
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/auth/logout", new { refreshToken = refresh })).EnsureSuccessStatusCode();

        var r = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Logout_all_sin_sesion_devuelve_401()
    {
        var res = await factory.CreateClient().PostAsync("/auth/logout-all", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Logout_all_revoca_todas_las_sesiones_del_usuario()
    {
        var auth = await RegisterAsync(factory);
        var refresh = auth.GetProperty("refreshToken").GetString();
        var access = auth.GetProperty("accessToken").GetString();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await client.PostAsync("/auth/logout-all", null)).EnsureSuccessStatusCode();

        var r = await factory.CreateClient().PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Reuso_dentro_de_la_ventana_de_gracia_no_mata_la_sesion_sucesora()
    {
        // Factory con gracia > 0 (el default es 0 = estricto).
        using var f = new GraceFactory();
        var client = f.CreateClient();
        var auth = await RegisterAsync(f);
        var r1 = auth.GetProperty("refreshToken").GetString();

        // Rotación normal: R1 → R2.
        var rot = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });
        rot.EnsureSuccessStatusCode();
        var r2 = (await rot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();

        // Reuso inmediato de R1 (dentro de la gracia): se rechaza…
        var reuse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // …pero NO se dispara la cascada: la sesión sucesora (R2) sigue viva.
        var r2use = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r2 });
        r2use.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reuso_de_un_token_rotado_revoca_toda_la_familia_por_defecto()
    {
        // Default: gracia = 0 → cualquier reuso de un token rotado se trata como robo.
        var client = factory.CreateClient();
        var auth = await RegisterAsync(factory);
        var r1 = auth.GetProperty("refreshToken").GetString();

        var rot = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });
        rot.EnsureSuccessStatusCode();
        var r2 = (await rot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();

        // Reuso de R1 → revoca toda la familia.
        var reuse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // R2 (que era válido) también queda revocado por la cascada.
        var r2use = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r2 });
        Assert.Equal(HttpStatusCode.Unauthorized, r2use.StatusCode);
    }

    [Fact]
    public async Task El_refresh_token_se_guarda_hasheado_no_en_claro()
    {
        using var f = new ApiFactory();
        var auth = await RegisterAsync(f);
        var refresh = auth.GetProperty("refreshToken").GetString()!;

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.RefreshTokens.AsNoTracking().Select(t => t.Token).ToListAsync();

        Assert.DoesNotContain(refresh, stored);                    // el valor en claro NO está en la BD
        Assert.Contains(TokenService.HashToken(refresh), stored);  // sí su hash
    }

    [Fact]
    public async Task Logout_invalida_tambien_el_access_token_actual()
    {
        var auth = await RegisterAsync(factory);
        var access = auth.GetProperty("accessToken").GetString();
        var refresh = auth.GetProperty("refreshToken").GetString();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await client.GetAsync("/auth/me")).EnsureSuccessStatusCode(); // antes vale

        // Logout enviando el access token (cabecera) y el refresh token (cuerpo).
        (await client.PostAsJsonAsync("/auth/logout", new { refreshToken = refresh })).EnsureSuccessStatusCode();

        // El mismo access token queda invalidado de inmediato.
        var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_all_invalida_el_access_token_actual()
    {
        var auth = await RegisterAsync(factory);
        var access = auth.GetProperty("accessToken").GetString();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await client.GetAsync("/auth/me")).EnsureSuccessStatusCode();

        (await client.PostAsync("/auth/logout-all", null)).EnsureSuccessStatusCode();

        var me = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_all_invalida_por_epoch_los_access_tokens_de_otros_dispositivos()
    {
        using var f = new ApiFactory();
        var email = $"u{Guid.NewGuid():N}@example.com";
        (await f.CreateClient().PostAsJsonAsync("/auth/register", new { email, password = Pwd })).EnsureSuccessStatusCode();

        // "Dispositivo B": su access token NO se usará para logout-all (jti distinto).
        var loginB = await (await f.CreateClient().PostAsJsonAsync("/auth/login", new { email, password = Pwd }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var accessB = loginB.GetProperty("accessToken").GetString();

        // Esperamos > 1 s para que el iat (granularidad de segundo) de B sea anterior al epoch.
        await Task.Delay(1100);

        // "Dispositivo A": inicia sesión y cierra TODAS las sesiones.
        var loginA = await (await f.CreateClient().PostAsJsonAsync("/auth/login", new { email, password = Pwd }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var clientA = f.CreateClient();
        clientA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginA.GetProperty("accessToken").GetString());
        (await clientA.PostAsync("/auth/logout-all", null)).EnsureSuccessStatusCode();

        // El access token de B (otro dispositivo, jti no incluido) queda revocado por epoch.
        var clientB = f.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessB);
        var me = await clientB.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    /// <summary>Fábrica con ventana de gracia de reuso > 0 (el default es 0 = estricto).</summary>
    private sealed class GraceFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Jwt:RefreshReuseGraceSeconds", "5");
        }
    }

    [Fact]
    public async Task Refresh_con_deadline_absoluto_pasado_se_rechaza()
    {
        using var f = new ApiFactory();
        await RegisterAsync(f); // crea un usuario válido
        const string token = "tok-absoluto-vencido";
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = (await db.Users.FirstAsync()).Id;
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                Token = TokenService.HashToken(token),          // en BD se guarda el hash
                FamilyId = Guid.NewGuid(),
                ExpiresAt = DateTime.UtcNow.AddDays(30),         // no expirado por inactividad
                AbsoluteExpiresAt = DateTime.UtcNow.AddDays(-1)  // pero deadline absoluto pasado
            });
            await db.SaveChangesAsync();
        }

        var r = await f.CreateClient().PostAsJsonAsync("/auth/refresh", new { refreshToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task La_rotacion_conserva_la_familia_y_el_deadline_absoluto()
    {
        using var f = new ApiFactory();
        var client = f.CreateClient();
        var auth = await RegisterAsync(f);
        var r1 = auth.GetProperty("refreshToken").GetString();

        // R1 → R2 → R3 (dos rotaciones).
        var r2 = (await (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r2 })).EnsureSuccessStatusCode();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = await db.RefreshTokens.AsNoTracking().ToListAsync();
        Assert.Equal(3, tokens.Count);                                        // R1, R2, R3
        Assert.Single(tokens.Select(t => t.FamilyId).Distinct());            // misma familia
        Assert.Single(tokens.Select(t => t.AbsoluteExpiresAt).Distinct());   // mismo deadline absoluto (no recalculado)
    }

    [Fact]
    public async Task Refresh_con_expiracion_por_inactividad_se_rechaza()
    {
        using var f = new ApiFactory();
        await RegisterAsync(f);
        const string token = "tok-inactividad-vencida";
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userId = (await db.Users.FirstAsync()).Id;
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                Token = TokenService.HashToken(token),
                FamilyId = Guid.NewGuid(),
                ExpiresAt = DateTime.UtcNow.AddDays(-1),         // expirado por inactividad
                AbsoluteExpiresAt = DateTime.UtcNow.AddDays(30)  // pero deadline absoluto futuro
            });
            await db.SaveChangesAsync();
        }

        var r = await f.CreateClient().PostAsJsonAsync("/auth/refresh", new { refreshToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task PurgeAsync_borra_los_tokens_muertos_y_conserva_los_vivos()
    {
        using var f = new ApiFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        const int retention = RefreshTokenCleanupService.RevokedRetentionDays;
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = "u", Token = "activo", ExpiresAt = now.AddDays(10), AbsoluteExpiresAt = now.AddDays(100) },
            new RefreshToken { UserId = "u", Token = "inactividad", ExpiresAt = now.AddDays(-1), AbsoluteExpiresAt = now.AddDays(100) },
            new RefreshToken { UserId = "u", Token = "absoluto", ExpiresAt = now.AddDays(10), AbsoluteExpiresAt = now.AddDays(-1) },
            new RefreshToken { UserId = "u", Token = "revocado-reciente", ExpiresAt = now.AddDays(10), AbsoluteExpiresAt = now.AddDays(100), RevokedAt = now.AddDays(-1) },
            new RefreshToken { UserId = "u", Token = "revocado-viejo", ExpiresAt = now.AddDays(10), AbsoluteExpiresAt = now.AddDays(100), RevokedAt = now.AddDays(-(retention + 1)) }
        );
        await db.SaveChangesAsync();

        var deleted = await RefreshTokenCleanupService.PurgeAsync(db, now);

        Assert.Equal(3, deleted); // inactividad + absoluto + revocado-viejo
        var remaining = await db.RefreshTokens.AsNoTracking().Select(t => t.Token).OrderBy(t => t).ToListAsync();
        Assert.Equal(new[] { "activo", "revocado-reciente" }, remaining);
    }
}
