using System.ComponentModel.DataAnnotations;

namespace ClashPlanner.Api.Models;

/// <summary>
/// Refresh token rotatorio persistido. Cada login emite uno; al refrescar se
/// revoca el anterior y se emite uno nuevo, de modo que un token solo es válido
/// una vez. Permite cerrar sesión revocando el token activo.
/// </summary>
public class RefreshToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Usuario propietario del token.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Valor opaco del token (aleatorio, alta entropía).</summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>Momento de expiración (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Momento de revocación (UTC), o null si sigue activo.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>¿Sigue siendo utilizable (no revocado y no expirado)?</summary>
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
