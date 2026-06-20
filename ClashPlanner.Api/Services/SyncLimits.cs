using ClashPlanner.Api.Dtos;

namespace ClashPlanner.Api.Services;

/// <summary>
/// Topes de tamaño del snapshot de sincronización. Evitan que un cliente
/// (autenticado) agote memoria/BD subiendo un payload desmesurado. Son límites muy
/// por encima del uso real de un jugador; ajustables aquí si hiciera falta.
/// El tope de bytes del cuerpo lo aplica Kestrel (<c>Limits:MaxRequestBodyBytes</c>);
/// esto limita la CARDINALIDAD (nº de entidades) y la longitud de los textos libres.
/// </summary>
public static class SyncLimits
{
    public const int MaxAccounts = 100;
    public const int MaxJobs = 20_000;
    public const int MaxBoosts = 10_000;
    public const int MaxHelperStates = 10_000;
    public const int MaxDeletions = 100_000;
    public const int MaxPlanItems = 50_000;
    /// <summary>Suma de entradas de los mapas (inventario + overrides + niveles de ayudantes).</summary>
    public const int MaxMapEntries = 100_000;
    /// <summary>Longitud máxima de los campos de texto libre (nombres, notas, etc.).</summary>
    public const int MaxStringLength = 4_000;

    /// <summary>
    /// Devuelve un mensaje describiendo el primer límite excedido, o <c>null</c> si el
    /// snapshot está dentro de los topes.
    /// </summary>
    public static string? Validate(SyncDataDto d)
    {
        if (d.Accounts.Count > MaxAccounts) return $"accounts ({d.Accounts.Count} > {MaxAccounts})";
        if (d.Jobs.Count > MaxJobs) return $"jobs ({d.Jobs.Count} > {MaxJobs})";
        if (d.Boosts.Count > MaxBoosts) return $"boosts ({d.Boosts.Count} > {MaxBoosts})";
        if (d.HelperStates.Count > MaxHelperStates) return $"helperStates ({d.HelperStates.Count} > {MaxHelperStates})";
        if (d.Deletions.Count > MaxDeletions) return $"deletions ({d.Deletions.Count} > {MaxDeletions})";

        var planItems = d.Plans.Values.Sum(p => p.Count);
        if (planItems > MaxPlanItems) return $"plans ({planItems} > {MaxPlanItems})";

        var mapEntries = d.Inventory.Values.Sum(v => v.Count)
                       + d.Overrides.Values.Sum(v => v.Count)
                       + d.HelperLevels.Values.Sum(v => v.Count);
        if (mapEntries > MaxMapEntries) return $"map-entries ({mapEntries} > {MaxMapEntries})";

        // Longitud de los campos de texto libre de cada entidad (coherente con el
        // comentario de MaxStringLength). El tope de bytes de Kestrel ya acota el total;
        // esto evita además strings sueltos desmesurados en BD.
        static bool TooLong(string? s) => (s?.Length ?? 0) > MaxStringLength;

        foreach (var a in d.Accounts)
            if (TooLong(a.Name) || TooLong(a.Tag)) return "account: campo de texto demasiado largo";
        foreach (var j in d.Jobs)
            if (TooLong(j.ItemName) || TooLong(j.Note)) return "job: campo de texto demasiado largo";
        foreach (var b in d.Boosts)
            if (TooLong(b.Name) || TooLong(b.BoostId)) return "boost: campo de texto demasiado largo";
        foreach (var h in d.HelperStates)
            if (TooLong(h.Name) || TooLong(h.Note)) return "helperState: campo de texto demasiado largo";
        foreach (var list in d.Plans.Values)
            foreach (var p in list)
                if (TooLong(p.ItemName)) return "plan: campo de texto demasiado largo";

        return null;
    }
}
