using System.ComponentModel.DataAnnotations;

namespace ClashPlanner.Api.Models;

/// <summary>
/// Token de la API oficial de Clash of Clans de un usuario, cifrado en reposo.
///
/// A diferencia del token por dispositivo del escritorio (atado a la IP de cada
/// PC), este token se autoriza para la IP FIJA del servidor, de modo que el
/// backend puede consultar la API de CoC en nombre del usuario desde cualquier
/// cliente (escritorio, web, móvil). El valor se guarda cifrado con ASP.NET Core
/// Data Protection; nunca se devuelve al cliente.
/// </summary>
public class CocTokenEntity
{
    [Key]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Token cifrado (Data Protection), nunca en claro en la BD.</summary>
    public string EncryptedToken { get; set; } = string.Empty;
}
