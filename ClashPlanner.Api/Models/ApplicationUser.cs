using Microsoft.AspNetCore.Identity;

namespace ClashPlanner.Api.Models;

/// <summary>
/// Usuario de la aplicación. Extiende el usuario de ASP.NET Core Identity
/// (email + contraseña). Cada usuario posee su propio conjunto de datos de
/// sincronización (cuentas, mejoras, etc.).
/// </summary>
public class ApplicationUser : IdentityUser
{
}
