using Microsoft.Extensions.Caching.Memory;

namespace ClashPlanner.Api.Services;

/// <summary>
/// Revocación de access tokens (JWT, sin estado) EN MEMORIA, para que al cerrar sesión el
/// token deje de valer de inmediato sin tener que tocar la BD en cada petición (el camino
/// caliente debe evitar la BD: ver la nota de Azure SQL serverless en el proyecto).
///
/// <para>Dos mecanismos: una <b>denylist por <c>jti</c></b> (cierre de UN dispositivo) y un
/// <b>epoch por usuario</b> (cierre de TODAS las sesiones: invalida los tokens emitidos antes
/// del epoch). Las entradas caducan solas pasada la vida del access token, así que el coste
/// es acotado.</para>
///
/// <para><b>Límite conocido:</b> al ser en memoria, no se comparte entre instancias ni
/// sobrevive a un reinicio; tras un reinicio, un token revocado podría volver a valer hasta su
/// expiración (≤ vida del access token). Suficiente para un despliegue de 1 instancia; para
/// varias instancias haría falta una caché distribuida.</para>
/// </summary>
public sealed class TokenRevocationService(IMemoryCache cache)
{
    private static string JtiKey(string jti) => $"revoked-jti:{jti}";
    private static string UserKey(string userId) => $"revoked-user:{userId}";

    /// <summary>Revoca un access token concreto por su <c>jti</c> hasta que expire.</summary>
    public void RevokeJti(string jti, DateTimeOffset expiresAt) =>
        cache.Set(JtiKey(jti), true, expiresAt);

    /// <summary>
    /// Revoca TODOS los access tokens del usuario emitidos hasta ahora (los emitidos después
    /// siguen valiendo). <paramref name="until"/> = ahora + vida del access token: pasado ese
    /// momento ya no quedan tokens previos al epoch, así que la entrada puede caducar.
    /// El epoch se trunca al SEGUNDO porque el claim <c>iat</c> tiene esa granularidad: así un
    /// token re-emitido en el mismo segundo (tras volver a iniciar sesión) no se mata por error.
    /// </summary>
    public void RevokeAllForUser(string userId, DateTimeOffset until)
    {
        var epochSecond = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cache.Set(UserKey(userId), epochSecond, until);
    }

    /// <summary>¿Está revocado este access token (por jti o por epoch de usuario)?</summary>
    public bool IsRevoked(string? jti, string? userId, DateTimeOffset issuedAt)
    {
        if (jti is not null && cache.TryGetValue(JtiKey(jti), out _)) return true;
        if (userId is not null && cache.TryGetValue(UserKey(userId), out DateTimeOffset epoch) && issuedAt < epoch)
            return true;
        return false;
    }
}
