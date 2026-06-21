namespace ClashPlanner.Api.Dtos;

/// <summary>
/// DTOs del protocolo de sincronización. Reflejan EXACTAMENTE el tipo
/// <c>SyncData</c> de <c>@clash-planner/core</c> (serialización camelCase), de
/// modo que el cliente envía y recibe su estado sin transformaciones. Las
/// estructuras de mapa (inventario, niveles de ayudantes, cola, overrides)
/// conservan las claves que genera el cliente (accountId, itemId, nivel…).
/// </summary>
public class SyncDataDto
{
    public List<AccountDto> Accounts { get; set; } = [];
    public List<JobDto> Jobs { get; set; } = [];
    public List<BoostDto> Boosts { get; set; } = [];
    public List<HelperStateDto> HelperStates { get; set; } = [];
    /// <summary>accountId → (helperId → nivel).</summary>
    public Dictionary<string, Dictionary<string, int>> HelperLevels { get; set; } = [];
    /// <summary>accountId → (itemId → inventario del objeto).</summary>
    public Dictionary<string, Dictionary<string, InventoryEntryDto>> Inventory { get; set; } = [];
    /// <summary>accountId → cola ordenada de planificación.</summary>
    public Dictionary<string, List<PlanItemDto>> Plans { get; set; } = [];
    /// <summary>itemId → (nivel → override de tiempo/coste).</summary>
    public Dictionary<string, Dictionary<string, OverrideEntryDto>> Overrides { get; set; } = [];
    /// <summary>Sello LWW propio de los overrides (globales, no por cuenta). Ver auditoría F-005.</summary>
    public long OverridesModifiedAt { get; set; }
    /// <summary>Lápidas de borrados (tombstones) para propagar eliminaciones.</summary>
    public List<TombstoneDto> Deletions { get; set; } = [];
}

/// <summary>Lápida de un borrado (tombstone).</summary>
public class TombstoneDto
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public long ModifiedAt { get; set; }
}

public class PlanWindowDto
{
    public bool Enabled { get; set; }
    public int StartMin { get; set; }
    public int EndMin { get; set; }
}

public class AccountDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Tag { get; set; }
    public string Color { get; set; } = string.Empty;
    public int ThLevel { get; set; }
    public int Builders { get; set; }
    public int BhLevel { get; set; }
    public int BbBuilders { get; set; }
    public int GoldPass { get; set; }
    public PlanWindowDto? PlanWindow { get; set; }
    public long? ModifiedAt { get; set; }
}

public class HelperAppliedDto
{
    public string HelperId { get; set; } = string.Empty;
    public double Hours { get; set; }
}

public class JobDto
{
    public string Id { get; set; } = string.Empty;
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
    public List<HelperAppliedDto>? HelpersApplied { get; set; }
    public long? ModifiedAt { get; set; }
}

public class BoostDto
{
    public string Id { get; set; } = string.Empty;
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

public class HelperStateDto
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string HelperId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long StartedAt { get; set; }
    public int CooldownSeconds { get; set; }
    public string? Note { get; set; }
    public long? ModifiedAt { get; set; }
}

public class BreakdownDto
{
    public int Level { get; set; }
    public int Count { get; set; }
}

public class InventoryEntryDto
{
    public List<BreakdownDto> Breakdown { get; set; } = [];
}

public class PlanItemDto
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemImage { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public int FromLevel { get; set; }
    public int ToLevel { get; set; }
    public string? Resource { get; set; }
}

public class OverrideEntryDto
{
    public int? TimeSeconds { get; set; }
    public int? Cost { get; set; }
}

/// <summary>Cuerpo de un push: snapshot completo + revisión base del cliente.</summary>
public class PushRequest
{
    public long BaseRevision { get; set; }
    public SyncDataDto Data { get; set; } = new();
}

/// <summary>
/// Respuesta de pull/push: la revisión vigente y, en pull o conflicto, el
/// snapshot del servidor. En un push aceptado <c>Data</c> es null (el cliente ya
/// lo tiene). En conflicto, <c>Conflict</c> es true y <c>Data</c> trae el estado
/// del servidor para que el cliente fusione.
/// </summary>
public class SyncResponse
{
    public long Revision { get; set; }
    public bool Conflict { get; set; }
    public SyncDataDto? Data { get; set; }
    /// <summary>Hora UTC del servidor (epoch ms) al responder, para que el cliente
    /// calibre el offset de su reloj (sellos `modifiedAt` en la línea del servidor).</summary>
    public long ServerTime { get; set; }
}
