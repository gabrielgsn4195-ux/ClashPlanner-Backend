namespace ClashPlanner.Api.Dtos;

/// <summary>
/// Ajuste GLOBAL de defensas artesanales activas (Estación de Forja). Refleja el
/// `craftedConfig` de <c>@clash-planner/core</c> (camelCase): `ActiveGroups` con
/// los dataId de los grupos (defensas) planificables, o <c>null</c> = modo
/// automático (decide el flag del catálogo del cliente).
/// </summary>
public class CraftedConfigDto
{
    public List<int>? ActiveGroups { get; set; }
}
