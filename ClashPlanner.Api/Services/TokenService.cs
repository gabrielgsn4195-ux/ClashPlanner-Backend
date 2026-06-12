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

    /// <summary>
    /// Crea un access token JWT con el id (sub) y el email del usuario.
    /// </summary>
    public string CreateAccessToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_s.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
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
    public RefreshToken CreateRefreshToken(string userId)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var value = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new RefreshToken
        {
            UserId = userId,
            Token = value,
            ExpiresAt = DateTime.UtcNow.AddDays(_s.RefreshTokenDays)
        };
    }
}
