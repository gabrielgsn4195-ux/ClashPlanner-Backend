using System.ComponentModel.DataAnnotations;

namespace ClashPlanner.Api.Models;

/// <summary>
/// Ajuste GENERAL de la aplicación, guardado en la tabla `Settings` como par
/// clave/valor (p. ej. `Coc:UseProxy = true`, `Coc:Token = &lt;token&gt;`). Es
/// configuración de servidor, separada de los «valores de mejora» (catálogo),
/// que viven en los datos de sincronización de cada usuario.
///
/// Los ajustes marcados como secretos (<see cref="IsSecret"/>) se guardan
/// CIFRADOS en reposo con ASP.NET Core Data Protection; nunca en claro ni se
/// devuelven al cliente.
/// </summary>
public class AppSetting
{
    /// <summary>Clave única del ajuste (p. ej. <c>Coc:UseProxy</c>).</summary>
    [Key]
    [MaxLength(128)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Valor (en claro para los normales; cifrado para los secretos).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Si el valor es un secreto y por tanto se cifra en reposo.</summary>
    public bool IsSecret { get; set; }

    /// <summary>Última modificación (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
