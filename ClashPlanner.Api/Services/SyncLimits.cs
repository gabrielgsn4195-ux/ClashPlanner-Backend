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
    /// <summary>Tope del número de CLAVES de primer nivel de los mapas (accountId / itemId).</summary>
    public const int MaxMapKeys = 10_000;
    /// <summary>Longitud máxima de una clave de mapa que acaba en columna de BD.</summary>
    public const int MaxKeyLength = 256;

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

        // Nº de CLAVES de primer nivel de los mapas (accountId / itemId): el contador de
        // entradas internas no acota cuántas claves hay, y cada clave de Plans/Inventory
        // genera filas/escrituras. Se acota aparte del tope de bytes de Kestrel. F-020.
        if (d.Plans.Count > MaxMapKeys) return $"plans-keys ({d.Plans.Count} > {MaxMapKeys})";
        if (d.Inventory.Count > MaxMapKeys) return $"inventory-keys ({d.Inventory.Count} > {MaxMapKeys})";
        if (d.HelperLevels.Count > MaxMapKeys) return $"helperLevels-keys ({d.HelperLevels.Count} > {MaxMapKeys})";
        if (d.Overrides.Count > MaxMapKeys) return $"overrides-keys ({d.Overrides.Count} > {MaxMapKeys})";
        if (d.AccountMapsModifiedAt.Count > MaxMapKeys) return $"accountMaps-keys ({d.AccountMapsModifiedAt.Count} > {MaxMapKeys})";

        // Longitud de las claves que acaban en columnas de BD (PK de Plans, etc.).
        static bool AnyKeyTooLong(IEnumerable<string> keys) => keys.Any(k => k.Length > MaxKeyLength);
        if (AnyKeyTooLong(d.Plans.Keys) || AnyKeyTooLong(d.Inventory.Keys)
            || AnyKeyTooLong(d.HelperLevels.Keys) || AnyKeyTooLong(d.Overrides.Keys))
            return "map: clave demasiado larga";

        // Longitud de los campos de texto libre de cada entidad (coherente con el
        // comentario de MaxStringLength). El tope de bytes de Kestrel ya acota el total;
        // esto evita además strings sueltos desmesurados en BD.
        static bool TooLong(string? s) => (s?.Length ?? 0) > MaxStringLength;
        // Los Id de entidad y las claves de lápida son columnas PK ACOTADAS (nvarchar(450)):
        // un valor sobredimensionado haría fallar el INSERT (500). Se acotan como las demás
        // claves para devolver un 413 limpio en su lugar. F-020.
        static bool TooLongKey(string? s) => (s?.Length ?? 0) > MaxKeyLength;

        foreach (var a in d.Accounts)
            if (TooLongKey(a.Id) || TooLong(a.Name) || TooLong(a.Tag)) return "account: campo de texto demasiado largo";
        foreach (var j in d.Jobs)
            if (TooLongKey(j.Id) || TooLong(j.ItemName) || TooLong(j.Note)) return "job: campo de texto demasiado largo";
        foreach (var b in d.Boosts)
            if (TooLongKey(b.Id) || TooLong(b.Name) || TooLong(b.BoostId)) return "boost: campo de texto demasiado largo";
        foreach (var h in d.HelperStates)
            if (TooLongKey(h.Id) || TooLong(h.Name) || TooLong(h.Note)) return "helperState: campo de texto demasiado largo";
        foreach (var t in d.Deletions)
            if (TooLongKey(t.Kind) || TooLongKey(t.Id)) return "deletion: clave demasiado larga";
        foreach (var list in d.Plans.Values)
            foreach (var p in list)
                if (TooLong(p.ItemName)) return "plan: campo de texto demasiado largo";

        return null;
    }
}
