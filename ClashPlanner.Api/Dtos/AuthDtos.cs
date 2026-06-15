using System.ComponentModel.DataAnnotations;

namespace ClashPlanner.Api.Dtos;

/// <summary>Alta de usuario por email y contraseña.</summary>
public class RegisterRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
}

/// <summary>Inicio de sesión por email y contraseña.</summary>
public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

/// <summary>Petición de refresco con el refresh token vigente.</summary>
public class RefreshRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>Petición de cierre de sesión (revoca el refresh token).</summary>
public class LogoutRequest
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Respuesta de autenticación: access token JWT (corto) y refresh token
/// (largo, rotatorio) para renovar el acceso sin volver a introducir credenciales.
/// </summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>Roles del usuario (Admin/Tecnico/Usuario) para el gating de la UI.</summary>
    public string[] Roles { get; set; } = [];
}
