using ClashPlanner.Api.Data;
using ClashPlanner.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ClashPlanner.Api.Services;

/// <summary>Claves conocidas de configuración general (tabla `Settings`).</summary>
public static class SettingKeys
{
    public const string CocToken = "Coc:Token";              // secreto (token de la API)
    public const string CocUseProxy = "Coc:UseProxy";        // bool
    public const string CocProxyUrl = "Coc:ProxyUrl";        // url base con proxy
    public const string CocDirectUrl = "Coc:DirectUrl";      // url base directa
    public const string CocTimeoutSeconds = "Coc:TimeoutSeconds"; // int
    public const string RateLimitCocPerMinute = "RateLimit:CocPerMinute"; // int (aplica al reiniciar)
    public const string CorsOrigins = "Cors:Origins";        // csv (aplica al reiniciar)
    public const string EventsConfig = "Events:Config";      // JSON de los eventos globales

    /// <summary>URL base de la API oficial de CoC (acceso directo). Valor por defecto
    /// de <see cref="CocDirectUrl"/> y semilla de la tabla `Settings`.</summary>
    public const string DefaultCocDirectUrl = "https://api.clashofclans.com/v1";

    /// <summary>URL base del proxy de RoyaleAPI (IP fija; útil cuando la IP del
    /// servidor cambia). Valor por defecto de <see cref="CocProxyUrl"/> y semilla.</summary>
    public const string DefaultCocProxyUrl = "https://cocproxy.royaleapi.dev/v1";

    /// <summary>Claves cuyo valor se guarda cifrado y se enmascara al leerlo.</summary>
    public static readonly IReadOnlySet<string> Secrets = new HashSet<string>(StringComparer.Ordinal) { CocToken };

    public static bool IsSecretKey(string key) => Secrets.Contains(key);
}

/// <summary>Vista de un ajuste para el cliente (los secretos van enmascarados).</summary>
public record SettingView(string Key, string Value, bool IsSecret, bool IsSet, DateTime UpdatedAt);

/// <summary>
/// Lee y escribe la configuración general en la tabla `Settings`. Cachea el mapa
/// completo unos minutos (se invalida al guardar). Los secretos se cifran con
/// Data Protection: en disco/BD nunca están en claro, y al listarlos se devuelven
/// enmascarados.
/// </summary>
public class AppSettingsService(
    AppDbContext db,
    IDataProtectionProvider dpp,
    IMemoryCache cache,
    ILogger<AppSettingsService> logger)
{
    private readonly IDataProtector _protector = dpp.CreateProtector("ClashPlanner.Settings.v1");
    private const string CacheKey = "appsettings:map";

    private async Task<Dictionary<string, string>> MapAsync() =>
        await cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        }) ?? new Dictionary<string, string>();

    /// <summary>Devuelve el valor (descifrando si es secreto), o null si no existe.</summary>
    public async Task<string?> GetStringAsync(string key)
    {
        var map = await MapAsync();
        if (!map.TryGetValue(key, out var raw) || raw is null) return null;
        if (!SettingKeys.IsSecretKey(key)) return raw;
        if (raw.Length == 0) return string.Empty;
        try { return _protector.Unprotect(raw); }
        catch (Exception e)
        {
            // No logueamos el valor (es secreto). Suele ocurrir si cambiaron las claves
            // de Data Protection: el secreto guardado ya no se puede descifrar.
            logger.LogWarning(e, "No se pudo descifrar el ajuste cifrado '{Key}'. ¿Cambiaron las claves de Data Protection?", key);
            return null;
        }
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback) =>
        bool.TryParse(await GetStringAsync(key), out var b) ? b : fallback;

    public async Task<int> GetIntAsync(string key, int fallback) =>
        int.TryParse(await GetStringAsync(key), out var n) ? n : fallback;

    /// <summary>Crea o actualiza un ajuste (cifra el valor si la clave es secreta).</summary>
    public async Task SetAsync(string key, string value)
    {
        var secret = SettingKeys.IsSecretKey(key);
        var stored = secret && value.Length > 0 ? _protector.Protect(value) : value;
        var row = await db.Settings.FindAsync(key);
        if (row is null)
            db.Settings.Add(new AppSetting { Key = key, Value = stored, IsSecret = secret, UpdatedAt = DateTime.UtcNow });
        else
        {
            row.Value = stored;
            row.IsSecret = secret;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        cache.Remove(CacheKey);
    }

    /// <summary>Siembra un ajuste solo si la clave no existe todavía.</summary>
    public async Task<bool> SeedAsync(string key, string? value)
    {
        if (value is null) return false;
        if (await db.Settings.AnyAsync(s => s.Key == key)) return false;
        await SetAsync(key, value);
        return true;
    }

    /// <summary>Lista todos los ajustes con los secretos enmascarados (para el cliente).</summary>
    public async Task<List<SettingView>> ListMaskedAsync()
    {
        var rows = await db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync();
        return rows.Select(r => new SettingView(
            r.Key,
            r.IsSecret ? (string.IsNullOrEmpty(r.Value) ? "" : "********") : r.Value,
            r.IsSecret,
            !string.IsNullOrEmpty(r.Value),
            r.UpdatedAt)).ToList();
    }
}
