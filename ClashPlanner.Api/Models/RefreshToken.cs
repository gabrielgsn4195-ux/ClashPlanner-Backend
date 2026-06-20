using System.ComponentModel.DataAnnotations;

namespace ClashPlanner.Api.Models;

/// <summary>
/// Refresh token rotatorio persistido. Cada login emite uno; al refrescar se
/// revoca el anterior y se emite uno nuevo (de la misma <see cref="FamilyId">familia</see>),
/// de modo que un token solo es válido una vez. Permite cerrar sesión revocando el
/// token activo. El reuso de un token ya rotado revoca toda la familia (señal de robo).
/// </summary>
public class RefreshToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Usuario propietario del token.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// HASH (SHA-256, base64) del valor del token. El valor en claro (256 bits aleatorios)
    /// solo lo conoce el cliente; en la BD guardamos su hash, de modo que una lectura de la
    /// base de datos no permita secuestrar sesiones. El índice único sigue valiendo (hashes
    /// únicos). La búsqueda hashea el token entrante y compara.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Identificador de la CADENA de rotación (login → refresh → refresh…). Se fija al
    /// emitir el primer token de la sesión y se conserva en cada rotación, de modo que el
    /// reuso de un token ya rotado pueda revocar toda la familia. Nullable: los tokens
    /// emitidos antes de esta versión no la tienen (se cae a revocar por usuario).
    /// </summary>
    public Guid? FamilyId { get; set; }

    /// <summary>Momento de expiración por inactividad (UTC); se renueva en cada rotación.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Deadline ABSOLUTO de la sesión (UTC): se fija al iniciar sesión y NO se renueva al
    /// rotar, de modo que una sesión no es eternamente renovable. Nullable: los tokens
    /// previos a esta versión no lo tienen (siguen solo bajo expiración por inactividad).
    /// </summary>
    public DateTime? AbsoluteExpiresAt { get; set; }

    /// <summary>Momento de revocación (UTC), o null si sigue activo.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>¿Sigue siendo utilizable (no revocado, ni expirado por inactividad ni por deadline absoluto)?</summary>
    public bool IsActive =>
        RevokedAt is null
        && DateTime.UtcNow < ExpiresAt
        && (AbsoluteExpiresAt is null || DateTime.UtcNow < AbsoluteExpiresAt);
}
