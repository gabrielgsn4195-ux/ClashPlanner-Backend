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
    /// <summary>Vida del refresh token (largo).</summary>
    public int RefreshTokenDays { get; set; } = 30;
}
