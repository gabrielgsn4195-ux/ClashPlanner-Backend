using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClashPlanner.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClashPlanner.Api.Services;

/// <summary>
/// Emite los tokens de autenticación: un access token JWT firmado (HS256) con la
/// identidad del usuario y un refresh token opaco de alta entropía para renovar
/// el acceso. No persiste nada: la rotación/almacenamiento del refresh token lo
/// gestiona el endpoint de auth contra la base de datos.
/// </summary>
public class TokenService(IOptions<JwtSettings> settings)
{
    private readonly JwtSettings _s = settings.Value;

    /// <summary>Segundos de vida del access token (para la respuesta de auth).</summary>
    public int AccessTokenSeconds => _s.AccessTokenMinutes * 60;

    /// <summary>Ventana de gracia para el reuso de un token recién rotado (ver <see cref="JwtSettings"/>).</summary>
    public TimeSpan RefreshReuseGrace => TimeSpan.FromSeconds(_s.RefreshReuseGraceSeconds);

    /// <summary>Hash estable (SHA-256, base64) de un refresh token, para guardar/buscar sin el valor en claro.</summary>
    public static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Crea un access token JWT con el id (sub) y el email del usuario.
    /// </summary>
    public string CreateAccessToken(ApplicationUser user, IEnumerable<string>? roles = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_s.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // iat explícito (epoch en segundos) para la revocación por epoch de usuario (logout-all).
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        // El claim de rol se valida con RoleClaimType = "role" (ver Program.cs).
        if (roles is not null)
            claims.AddRange(roles.Select(r => new Claim("role", r)));
        var token = new JwtSecurityToken(
            issuer: _s.Issuer,
            audience: _s.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_s.AccessTokenMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Crea un refresh token sin persistir (256 bits aleatorios en base64url).
    /// </summary>
    /// <param name="userId">Usuario propietario.</param>
    /// <param name="familyId">Familia de rotación; si es null, abre una nueva (login/registro).</param>
    /// <param name="absoluteExpiresAt">Deadline absoluto a conservar al rotar; si es null, se calcula uno nuevo.</param>
    /// <returns>La entidad a persistir (con el HASH del token) y el valor en CLARO para el cliente.</returns>
    public (RefreshToken Entity, string Plaintext) CreateRefreshToken(
        string userId, Guid? familyId = null, DateTime? absoluteExpiresAt = null)
    {
        var now = DateTime.UtcNow;
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var entity = new RefreshToken
        {
            UserId = userId,
            Token = HashToken(value), // en la BD solo el hash
            FamilyId = familyId ?? Guid.NewGuid(),
            ExpiresAt = now.AddDays(_s.RefreshTokenDays),
            AbsoluteExpiresAt = absoluteExpiresAt ?? now.AddDays(_s.RefreshTokenAbsoluteDays)
        };
        return (entity, value);
    }
}
