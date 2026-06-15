namespace ClashPlanner.Api.Models;

/// <summary>
/// Roles de la aplicación:
///  - <see cref="Admin"/>: todo (editar configuración, gestionar usuarios/roles, catálogo).
///  - <see cref="Tecnico"/>: configuración en solo lectura + editar el catálogo (valores de mejora).
///  - <see cref="Usuario"/>: uso normal (aldeas, planificación, sync); sin acceso a administración.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Tecnico = "Tecnico";
    public const string Usuario = "Usuario";

    /// <summary>Todos los roles (para sembrar y validar asignaciones).</summary>
    public static readonly string[] All = { Admin, Tecnico, Usuario };
}
