using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Tests de integración del backend: autenticación y ciclo de sincronización
/// (pull/push/conflicto) contra una API real con SQLite en memoria.
/// </summary>
public class SyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registra un usuario único y devuelve un cliente autenticado.</summary>
    private async Task<HttpClient> RegisterAndAuthAsync()
    {
        var client = factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var res = await client.PostAsJsonAsync("/auth/register", new { email, password = "Passw0rd!23" });
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<JsonElement>();
        var token = auth.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Snapshot mínimo con una cuenta.</summary>
    private static object SnapshotWithAccount(string name, int goldPass = 0) => new
    {
        accounts = new[]
        {
            new { id = "acc-1", name, color = "#fff", thLevel = 15, builders = 5, bhLevel = 10, bbBuilders = 2, goldPass, modifiedAt = 1L }
        },
        jobs = Array.Empty<object>(),
        boosts = Array.Empty<object>(),
        helperStates = Array.Empty<object>(),
        helperLevels = new { },
        inventory = new { },
        plans = new { },
        overrides = new { }
    };

    [Fact]
    public async Task Health_responde_ok()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/health");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Sync_requiere_autenticacion()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/sync");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_con_credenciales_invalidas_devuelve_401()
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/login", new { email = "noexiste@example.com", password = "loquesea123" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Pull_inicial_esta_vacio_en_revision_cero()
    {
        var client = await RegisterAndAuthAsync();
        var pull = await (await client.GetAsync("/sync")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, pull.GetProperty("revision").GetInt64());
        Assert.Empty(pull.GetProperty("data").GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task Pull_devuelve_serverTime_para_calibrar_el_reloj()
    {
        var client = await RegisterAndAuthAsync();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pull = await (await client.GetAsync("/sync")).Content.ReadFromJsonAsync<JsonElement>();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var serverTime = pull.GetProperty("serverTime").GetInt64();
        Assert.InRange(serverTime, before, after);
    }

    [Fact]
    public async Task Push_luego_pull_devuelve_los_datos_y_sube_la_revision()
    {
        var client = await RegisterAndAuthAsync();

        var push = await client.PostAsJsonAsync("/sync", new { baseRevision = 0, data = SnapshotWithAccount("Mi Aldea", 20) });
        push.EnsureSuccessStatusCode();
        var pushBody = await push.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, pushBody.GetProperty("revision").GetInt64());

        var pull = await (await client.GetAsync("/sync")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, pull.GetProperty("revision").GetInt64());
        var accounts = pull.GetProperty("data").GetProperty("accounts");
        Assert.Single(accounts.EnumerateArray());
        Assert.Equal("Mi Aldea", accounts[0].GetProperty("name").GetString());
        Assert.Equal(20, accounts[0].GetProperty("goldPass").GetInt32());
    }

    [Fact]
    public async Task Push_con_revision_base_desfasada_devuelve_409_con_estado_servidor()
    {
        var client = await RegisterAndAuthAsync();
        await client.PostAsJsonAsync("/sync", new { baseRevision = 0, data = SnapshotWithAccount("Primera") });

        // Segundo push con baseRevision obsoleta (0) → conflicto.
        var conflict = await client.PostAsJsonAsync("/sync", new { baseRevision = 0, data = SnapshotWithAccount("Segunda") });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("conflict").GetBoolean());
        Assert.Equal(1, body.GetProperty("revision").GetInt64());
        // El cuerpo del conflicto trae el estado del servidor (la «Primera»).
        Assert.Equal("Primera", body.GetProperty("data").GetProperty("accounts")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Las_lapidas_de_borrado_se_persisten_y_se_devuelven()
    {
        var client = await RegisterAndAuthAsync();
        var data = new
        {
            accounts = Array.Empty<object>(),
            jobs = Array.Empty<object>(),
            boosts = Array.Empty<object>(),
            helperStates = Array.Empty<object>(),
            helperLevels = new { },
            inventory = new { },
            plans = new { },
            overrides = new { },
            deletions = new[] { new { kind = "job", id = "j1", modifiedAt = 1700000000000L } }
        };
        var push = await client.PostAsJsonAsync("/sync", new { baseRevision = 0, data });
        push.EnsureSuccessStatusCode();

        var pull = await (await client.GetAsync("/sync")).Content.ReadFromJsonAsync<JsonElement>();
        var tombs = pull.GetProperty("data").GetProperty("deletions");
        Assert.Single(tombs.EnumerateArray());
        Assert.Equal("job", tombs[0].GetProperty("kind").GetString());
        Assert.Equal("j1", tombs[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Los_datos_estan_aislados_por_usuario()
    {
        var a = await RegisterAndAuthAsync();
        var b = await RegisterAndAuthAsync();
        await a.PostAsJsonAsync("/sync", new { baseRevision = 0, data = SnapshotWithAccount("De A") });

        var pullB = await (await b.GetAsync("/sync")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(pullB.GetProperty("data").GetProperty("accounts").EnumerateArray());
    }

    [Fact]
    public async Task Refresh_rota_el_token_y_el_anterior_deja_de_valer()
    {
        var client = factory.CreateClient();
        var email = $"u{Guid.NewGuid():N}@example.com";
        var reg = await (await client.PostAsJsonAsync("/auth/register", new { email, password = "Passw0rd!23" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var refresh = reg.GetProperty("refreshToken").GetString();

        var r1 = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        r1.EnsureSuccessStatusCode();

        // Reusar el refresh token ya rotado debe fallar.
        var r2 = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }
}
