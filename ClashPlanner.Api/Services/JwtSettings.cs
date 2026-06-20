namespace ClashPlanner.Api.Services;

/// <summary>
/// Parámetros de los tokens, cargados de la configuración (sección «Jwt»). La
/// clave de firma NUNCA debe estar en código: se inyecta por user-secrets en
/// desarrollo y por variable de entorno / secret manager en producción.
/// </summary>
public class JwtSettings
{
    public string Issuer { get; set; } = "ClashPlanner";
    public string Audience { get; set; } = "ClashPlanner";
    /// <summary>Clave simétrica de firma HS256 (mínimo 32 bytes).</summary>
    public string SigningKey { get; set; } = string.Empty;
    /// <summary>Vida del access token (corto).</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>Vida del refresh token por INACTIVIDAD (se renueva en cada rotación).</summary>
    public int RefreshTokenDays { get; set; } = 30;
    /// <summary>Deadline ABSOLUTO de la sesión (no se renueva al rotar); pasado, hay que volver a iniciar sesión.</summary>
    public int RefreshTokenAbsoluteDays { get; set; } = 180;
    /// <summary>
    /// Ventana de gracia (segundos) tras rotar un token: si el MISMO token recién rotado se
    /// presenta de nuevo dentro de ella (doble envío de un cliente que reintenta), se rechaza
    /// pero NO se dispara la revocación en cascada (que mataría la sesión sucesora activa).
    /// Pasada la ventana, un token revocado reaparecido se trata como robo y revoca la familia.
    ///
    /// <para>Por DEFECTO 0 (reuso estricto: cualquier token revocado reaparecido dispara la
    /// cascada): el cliente oficial hace un único intento de refresco —no reenvía—, así que la
    /// gracia no aporta y solo abriría una ventana en la que un token robado y rotado por el
    /// atacante no se detectaría. Súbela solo si un cliente puede reenviar el mismo refresh.</para>
    /// </summary>
    public int RefreshReuseGraceSeconds { get; set; } = 0;
}
