using System.Text.Json;

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
public class CocService(IHttpClientFactory httpFactory, AppSettingsService settings, IConfiguration config)
{
    private const string DefaultDirect = "https://api.clashofclans.com/v1";
    private const string DefaultProxy = "https://cocproxy.royaleapi.dev/v1";

    /// <summary>
    /// Consulta un jugador por etiqueta usando el token de servidor. El token, el
    /// proxy on/off, las URLs y el timeout se leen de la tabla `Settings` (con
    /// fallback a la config de arranque mientras la BD no esté sembrada).
    /// </summary>
    /// <param name="tag">Etiqueta del jugador (con o sin `#`).</param>
    public async Task<CocResult> GetPlayerAsync(string tag)
    {
        var token = await settings.GetStringAsync(SettingKeys.CocToken) ?? config["Coc:Token"];
        if (string.IsNullOrWhiteSpace(token))
            return new CocResult(false, 0, "{\"reason\":\"server-token-not-configured\"}");

        var useProxy = await settings.GetBoolAsync(SettingKeys.CocUseProxy, config.GetValue("Coc:UseProxy", true));
        var proxyUrl = await settings.GetStringAsync(SettingKeys.CocProxyUrl) ?? DefaultProxy;
        var directUrl = await settings.GetStringAsync(SettingKeys.CocDirectUrl) ?? DefaultDirect;
        var baseUrl = useProxy ? proxyUrl : directUrl;
        var timeoutSeconds = await settings.GetIntAsync(SettingKeys.CocTimeoutSeconds, 15);

        // Normaliza la etiqueta y la codifica (`#` → %23). Solo se construye
        // `players/<tag-codificado>`, evitando inyección de ruta.
        var normalized = tag.Trim().TrimStart('#');
        var encoded = Uri.EscapeDataString("#" + normalized);

        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/players/{encoded}");
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
            return new CocResult(false, 0, "{\"reason\":\"timeout\"}");
        }
        catch (Exception e)
        {
            return new CocResult(false, 0, $"{{\"reason\":\"network\",\"message\":{JsonSerializer.Serialize(e.Message)}}}");
        }
    }
}
