namespace ClashPlanner.Api.Services;

/// <summary>Resultado de una llamada proxy a la API de Clash of Clans.</summary>
public record CocResult(bool Ok, int Status, string? Json);

/// <summary>
/// Proxy de la API de Clash of Clans con UN ÚNICO token de servidor
/// (configuración `Coc:Token`). Todos los clientes (escritorio, web, móvil)
/// consultan a través de este proxy: el token vive solo en el servidor y el
/// usuario final NUNCA lo ve ni lo introduce.
///
/// El token de CoC está atado a IP; con `Coc:UseProxy = true` las llamadas salen
/// por el proxy de RoyaleAPI (IP fija 45.79.218.79), de modo que basta con
/// autorizar esa IP en el token. Con `false`, se llama directo a CoC y hay que
/// autorizar la IP pública del servidor.
/// </summary>
public class CocService(
    IHttpClientFactory httpFactory,
    AppSettingsService settings,
    IConfiguration config,
    ILogger<CocService> logger)
{
    // Las URLs por defecto viven en SettingKeys (compartidas con la semilla de Program.cs).

    /// <summary>Codifica una etiqueta de CoC (`#ABC` → `%23ABC`), normalizándola.</summary>
    private static string EncodeTag(string tag) => Uri.EscapeDataString("#" + tag.Trim().TrimStart('#'));

    /// <summary>Consulta un jugador por etiqueta. <see cref="FetchAsync"/>.</summary>
    public Task<CocResult> GetPlayerAsync(string tag) => FetchAsync($"players/{EncodeTag(tag)}");

    /// <summary>Información del clan por etiqueta.</summary>
    public Task<CocResult> GetClanAsync(string tag) => FetchAsync($"clans/{EncodeTag(tag)}");

    /// <summary>Guerra actual del clan (404/403 si no hay o el registro es privado).</summary>
    public Task<CocResult> GetCurrentWarAsync(string tag) => FetchAsync($"clans/{EncodeTag(tag)}/currentwar");

    /// <summary>Registro de guerras del clan (si es público).</summary>
    public Task<CocResult> GetWarLogAsync(string tag) => FetchAsync($"clans/{EncodeTag(tag)}/warlog");

    /// <summary>Temporadas de asalto de la Capital del clan.</summary>
    public Task<CocResult> GetCapitalRaidsAsync(string tag) => FetchAsync($"clans/{EncodeTag(tag)}/capitalraidseasons");

    /// <summary>Grupo de la Liga de Guerras de Clanes (CWL) de la temporada actual.</summary>
    public Task<CocResult> GetLeagueGroupAsync(string tag) => FetchAsync($"clans/{EncodeTag(tag)}/currentwar/leaguegroup");

    /// <summary>Una guerra de CWL por su etiqueta de guerra (`warTag`).</summary>
    public Task<CocResult> GetCwlWarAsync(string warTag) => FetchAsync($"clanwarleagues/wars/{EncodeTag(warTag)}");

    /// <summary>
    /// Hace un GET autenticado a un recurso de la API de CoC usando el token de
    /// servidor. El token, el proxy on/off, las URLs y el timeout se leen de la tabla
    /// `Settings` (con fallback a la config de arranque mientras la BD no esté sembrada).
    /// `path` es un recurso ya codificado (p. ej. `players/%23ABC`), construido en este
    /// servicio para evitar inyección de ruta.
    /// </summary>
    private async Task<CocResult> FetchAsync(string path)
    {
        // El token y los ajustes se leen de la VARIABLE DE ENTORNO (config) cuando está
        // definida, para NO tocar la tabla `Settings` en el camino caliente: con un Postgres
        // serverless como Neon (que se suspende por inactividad) leer la BD en cada `/coc`
        // colgaría la petición mientras la BD despierta. Solo si no hay token en config se cae
        // a la BD (token de la admin UI).
        string? token = config["Coc:Token"];
        bool useProxy;
        string proxyUrl;
        string directUrl;
        int timeoutSeconds;
        if (!string.IsNullOrWhiteSpace(token))
        {
            useProxy = config.GetValue("Coc:UseProxy", true);
            proxyUrl = config["Coc:ProxyUrl"] ?? SettingKeys.DefaultCocProxyUrl;
            directUrl = config["Coc:DirectUrl"] ?? SettingKeys.DefaultCocDirectUrl;
            timeoutSeconds = config.GetValue("Coc:TimeoutSeconds", 15);
        }
        else
        {
            token = await settings.GetStringAsync(SettingKeys.CocToken);
            if (string.IsNullOrWhiteSpace(token))
                return new CocResult(false, 0, "{\"reason\":\"server-token-not-configured\"}");
            useProxy = await settings.GetBoolAsync(SettingKeys.CocUseProxy, config.GetValue("Coc:UseProxy", true));
            proxyUrl = await settings.GetStringAsync(SettingKeys.CocProxyUrl) ?? SettingKeys.DefaultCocProxyUrl;
            directUrl = await settings.GetStringAsync(SettingKeys.CocDirectUrl) ?? SettingKeys.DefaultCocDirectUrl;
            timeoutSeconds = await settings.GetIntAsync(SettingKeys.CocTimeoutSeconds, 15);
        }

        var baseUrl = useProxy ? proxyUrl : directUrl;

        // Defensa en profundidad SSRF: solo http(s) absolutas. Aunque la admin UI ya valida
        // las URLs al escribirlas, un valor manipulado (config/BD) no debe poder dirigir el
        // token de servidor a esquemas peligrosos (file://, etc.).
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            logger.LogError("URL base de CoC inválida o con esquema no permitido: {BaseUrl}", baseUrl);
            return new CocResult(false, 0, "{\"reason\":\"server-misconfigured\"}");
        }

        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{path}");
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Headers.Add("Accept", "application/json");
        try
        {
            using var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return new CocResult(res.IsSuccessStatusCode, (int)res.StatusCode, body);
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Timeout ({Seconds}s) consultando la API de CoC: {Path}", timeoutSeconds, path);
            return new CocResult(false, 0, "{\"reason\":\"timeout\"}");
        }
        catch (Exception e)
        {
            // No exponemos el detalle de la excepción al cliente (podría revelar DNS,
            // rutas internas, etc.): va solo al log del servidor.
            logger.LogWarning(e, "Error de red consultando la API de CoC: {Path}", path);
            return new CocResult(false, 0, "{\"reason\":\"network\"}");
        }
    }
}
