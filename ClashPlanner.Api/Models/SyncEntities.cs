using System.ComponentModel.DataAnnotations;

namespace ClashPlanner.Api.Models;

/// <summary>
/// Entidades de SINCRONIZACIÓN: el reflejo en base de datos del estado del
/// planificador de cada usuario. Las claves primarias usan el id que genera el
/// cliente (UUID) junto al <c>UserId</c>, de modo que coinciden entre
/// dispositivos. Las subestructuras anidadas (inventario, niveles de ayudantes,
/// helpersApplied, ventana horaria, cola, overrides) se guardan como JSON en
/// columnas <c>nvarchar(max)</c>: el protocolo reemplaza el snapshot completo en
/// cada push, así que no necesitan normalizarse para esta primera versión.
/// </summary>

/// <summary>Una cuenta del juego del usuario (con sus dos aldeas).</summary>
public class AccountEntity
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Tag { get; set; }
    public string Color { get; set; } = string.Empty;
    public int ThLevel { get; set; }
    public int Builders { get; set; }
    public int BhLevel { get; set; }
    public int BbBuilders { get; set; }
    public int GoldPass { get; set; }
    /// <summary>Ventana horaria del asistente, serializada (o null).</summary>
    public string? PlanWindowJson { get; set; }
    /// <summary>Inventario de la cuenta (itemId → niveles), serializado.</summary>
    public string InventoryJson { get; set; } = "{}";
    /// <summary>Niveles de ayudantes de la cuenta (helperId → nivel), serializado.</summary>
    public string HelperLevelsJson { get; set; } = "{}";
    public long? ModifiedAt { get; set; }
    /// <summary>Sello LWW de los mapas de la cuenta (inventario/ayudantes/cola). Ver F-006.</summary>
    public long MapsModifiedAt { get; set; }
}

/// <summary>Una mejora en curso o programada.</summary>
public class JobEntity
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemImage { get; set; }
    public string Category { get; set; } = string.Empty;
    public int FromLevel { get; set; }
    public int ToLevel { get; set; }
    public string Slot { get; set; } = string.Empty;
    public long StartedAt { get; set; }
    public int DurationSeconds { get; set; }
    public string? Resource { get; set; }
    public int? Cost { get; set; }
    public string? Note { get; set; }
    public bool? Imported { get; set; }
    /// <summary>Ayudantes asignados (helperId/hours), serializado o null.</summary>
    public string? HelpersAppliedJson { get; set; }
    public long? ModifiedAt { get; set; }
}

/// <summary>Un boost activado, con su ventana de efecto.</summary>
public class BoostEntity
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public string BoostId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Multiplier { get; set; }
    public long StartedAt { get; set; }
    public int DurationSeconds { get; set; }
    public string AppliesTo { get; set; } = string.Empty;
    public int? CooldownSeconds { get; set; }
    public bool? Imported { get; set; }
    public long? ModifiedAt { get; set; }
}

/// <summary>Estado de refresco/cooldown de un ayudante.</summary>
public class HelperStateEntity
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string HelperId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long StartedAt { get; set; }
    public int CooldownSeconds { get; set; }
    public string? Note { get; set; }
    public long? ModifiedAt { get; set; }
}

/// <summary>Cola de planificación de una cuenta (lista ordenada, serializada).</summary>
public class PlanEntity
{
    [Required] public string UserId { get; set; } = string.Empty;
    [Required] public string AccountId { get; set; } = string.Empty;
    /// <summary>Lista ordenada de PlanItem, serializada.</summary>
    public string ItemsJson { get; set; } = "[]";
}

/// <summary>Overrides de catálogo del usuario (un único documento JSON).</summary>
public class OverrideEntity
{
    [Required] public string UserId { get; set; } = string.Empty;
    public string Json { get; set; } = "{}";
    /// <summary>Sello LWW propio de los overrides (globales). Ver auditoría F-005.</summary>
    public long ModifiedAt { get; set; }
}

/// <summary>
/// Lápida de un borrado (tombstone): permite que la eliminación de una entidad
/// se propague a otros dispositivos en la fusión en vez de resucitarla.
/// </summary>
public class DeletionEntity
{
    [Required] public string UserId { get; set; } = string.Empty;
    /// <summary>Tipo de entidad borrada (`account`/`job`/`boost`/`helperState`).</summary>
    [Required] public string Kind { get; set; } = string.Empty;
    /// <summary>Id de la entidad borrada.</summary>
    [Required] public string EntityId { get; set; } = string.Empty;
    public long ModifiedAt { get; set; }
}

/// <summary>
/// Revisión de sincronización por usuario: un contador que crece en cada push
/// aceptado. El cliente envía la revisión que tenía como base; si no coincide
/// con la del servidor, el push se rechaza (409) para forzar una fusión.
/// </summary>
public class UserSyncState
{
    [Required] public string UserId { get; set; } = string.Empty;
    /// <summary>
    /// Contador de revisión. Es token de concurrencia (<see cref="ConcurrencyCheckAttribute"/>):
    /// EF añade `WHERE Revision = @original` al actualizar, de modo que dos push concurrentes
    /// del mismo usuario no puedan ambos pasar (el 2.º falla con DbUpdateConcurrencyException
    /// y se trata como conflicto), evitando la pérdida de datos.
    /// </summary>
    [ConcurrencyCheck]
    public long Revision { get; set; }
}
