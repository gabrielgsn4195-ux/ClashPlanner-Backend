using System.Net.Http.Json;
using ClashPlanner.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ClashPlanner.Api.Services;

/// <summary>Resultado de una llamada proxy a la API de Clash of Clans.</summary>
public record CocResult(bool Ok, int Status, string? Json);

/// <summary>
/// Gestiona el token de Clash of Clans de cada usuario (cifrado en reposo con
/// Data Protection) y proxea las consultas a la API oficial usando la IP fija
/// del servidor. Centralizar el token permite a cualquier cliente (incluida la
/// web, que no puede llamar a CoC por CORS) consultar la API.
/// </summary>
public class CocService(AppDbContext db, IDataProtectionProvider dp, IHttpClientFactory httpFactory)
{
    private const string CocBase = "https://api.clashofclans.com/v1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    /// <summary>IP pública del servidor, cacheada tras la primera consulta.</summary>
    private static string? _serverIp;

    private IDataProtector Protector => dp.CreateProtector("ClashPlanner.CocToken");

    /// <summary>Guarda (cifrado) el token de CoC del usuario.</summary>
    public async Task SetTokenAsync(string userId, string token)
    {
        var enc = Protector.Protect(token);
        var existing = await db.CocTokens.FirstOrDefaultAsync(t => t.UserId == userId);
        if (existing is null) db.CocTokens.Add(new() { UserId = userId, EncryptedToken = enc });
        else existing.EncryptedToken = enc;
        await db.SaveChangesAsync();
    }

    /// <summary>Borra el token de CoC del usuario.</summary>
    public async Task ClearTokenAsync(string userId)
    {
        await db.CocTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync();
    }

    /// <summary>¿Tiene el usuario un token de CoC guardado?</summary>
    public async Task<bool> HasTokenAsync(string userId) =>
        await db.CocTokens.AnyAsync(t => t.UserId == userId);

    /// <summary>Token en claro del usuario (descifrado), o null si no hay o falla.</summary>
    private async Task<string?> GetTokenAsync(string userId)
    {
        var row = await db.CocTokens.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId);
        if (row is null) return null;
        try { return Protector.Unprotect(row.EncryptedToken); }
        catch { return null; }
    }

    /// <summary>
    /// Proxea la consulta de un jugador a la API de CoC con el token del usuario.
    /// </summary>
    /// <param name="userId">Usuario en cuyo nombre se consulta.</param>
    /// <param name="tag">Etiqueta del jugador (con o sin `#`).</param>
    public async Task<CocResult> GetPlayerAsync(string userId, string tag)
    {
        var token = await GetTokenAsync(userId);
        if (token is null) return new CocResult(false, 0, "{\"reason\":\"no-token\"}");

        // Normaliza la etiqueta y la codifica (el `#` → %23). Evita inyección de
        // ruta: solo se construye `players/<tag-codificado>`.
        var normalized = tag.Trim().TrimStart('#');
        var encoded = Uri.EscapeDataString("#" + normalized);

        using var http = httpFactory.CreateClient();
        http.Timeout = Timeout;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{CocBase}/players/{encoded}");
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
            return new CocResult(false, 0, $"{{\"reason\":\"network\",\"message\":{System.Text.Json.JsonSerializer.Serialize(e.Message)}}}");
        }
    }

    /// <summary>
    /// IP pública del servidor (cacheada): la que el usuario debe autorizar al
    /// crear su token en developer.clashofclans.com.
    /// </summary>
    public async Task<string?> GetServerIpAsync()
    {
        if (_serverIp is not null) return _serverIp;
        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            var doc = await http.GetFromJsonAsync<IpResponse>("https://api.ipify.org?format=json");
            _serverIp = doc?.Ip;
        }
        catch { /* sin IP: el usuario puede consultarla por otros medios */ }
        return _serverIp;
    }

    private record IpResponse(string Ip);
}
