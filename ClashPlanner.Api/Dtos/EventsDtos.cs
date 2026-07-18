namespace ClashPlanner.Api.Dtos;

/// <summary>
/// DTOs de los eventos GLOBALES del juego. Reflejan el tipo <c>GameEvent</c> de
/// <c>@clash-planner/core</c> (serialización camelCase). El servidor solo valida y
/// persiste la lista como JSON en la tabla `Settings`; no calcula efectos ni
/// evalúa ventanas temporales (eso lo hace cada cliente).
/// </summary>
public class GameEventDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Interruptor maestro: si es false, el evento nunca aplica.</summary>
    public bool Enabled { get; set; }
    /// <summary>Inicio programado (epoch ms). null = sin límite inferior.</summary>
    public long? StartsAt { get; set; }
    /// <summary>Fin programado (epoch ms). null = sin límite superior.</summary>
    public long? EndsAt { get; set; }
    public List<EventEffectDto> Effects { get; set; } = [];
    /// <summary>Si está activo, habilita el slot extra del Constructor Duende.</summary>
    public bool GoblinBuilder { get; set; }
    public EventBannerDto? Banner { get; set; }
}

/// <summary>Efecto de un evento sobre tiempo o coste, con filtros opcionales (AND).</summary>
public class EventEffectDto
{
    /// <summary>"time" o "cost".</summary>
    public string Target { get; set; } = "time";
    /// <summary>% de descuento (positivo reduce; negativo encarece/alarga).</summary>
    public double Percent { get; set; }
    /// <summary>Aldea: "home" | "builder".</summary>
    public string? Village { get; set; }
    /// <summary>Vía: "builder" | "lab" | "pet".</summary>
    public string? Track { get; set; }
    /// <summary>Categorías (vacío/ausente = todas).</summary>
    public List<string>? Categories { get; set; }
    /// <summary>Recurso (solo coste).</summary>
    public string? Resource { get; set; }
    /// <summary>
    /// Ayuntamiento mínimo (aldea del efecto: principal → TH, nocturna → Taller).
    /// null = sin mínimo. Permite descuentos distintos por TH (Mejoramanía real).
    /// </summary>
    public int? ThMin { get; set; }
    /// <summary>Ayuntamiento/Taller máximo. null = sin máximo.</summary>
    public int? ThMax { get; set; }
}

/// <summary>Rótulo informativo del evento (el mensaje admite emojis).</summary>
public class EventBannerDto
{
    public bool Show { get; set; }
    public string Message { get; set; } = string.Empty;
}
