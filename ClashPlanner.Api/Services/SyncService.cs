using System.Text.Json;
using ClashPlanner.Api.Data;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClashPlanner.Api.Services;

/// <summary>
/// Lógica de sincronización: traduce entre el snapshot del cliente
/// (<see cref="SyncDataDto"/>) y las entidades en base de datos, y aplica el
/// protocolo de revisión optimista.
///
/// <para>El servidor es deliberadamente simple: NO fusiona. Un push solo se
/// acepta si la revisión base del cliente coincide con la del servidor; entonces
/// reemplaza el snapshot completo del usuario en una transacción e incrementa la
/// revisión. Si no coincide, devuelve conflicto con el estado del servidor para
/// que el cliente fusione (last-write-wins por <c>modifiedAt</c>) y reintente.</para>
/// </summary>
public class SyncService(AppDbContext db, ILogger<SyncService> logger)
{
    /// <summary>Serialización camelCase, coherente con el cliente TS.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Lee el snapshot completo del usuario y su revisión actual.
    /// </summary>
    public async Task<SyncResponse> PullAsync(string userId)
    {
        var data = await ReadSnapshotAsync(userId);
        var revision = await GetRevisionAsync(userId);
        return new SyncResponse
        {
            Revision = revision,
            Data = data,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    /// Aplica un push si la revisión base coincide; si no, devuelve conflicto.
    /// </summary>
    /// <param name="userId">Usuario propietario de los datos.</param>
    /// <param name="req">Snapshot a guardar y revisión base del cliente.</param>
    public async Task<SyncResponse> PushAsync(string userId, PushRequest req)
    {
        // `EnableRetryOnFailure` (estrategia de reintento de Npgsql) NO admite
        // transacciones iniciadas a mano: hay que ejecutarlas dentro de una estrategia de
        // ejecución para que el bloque se reintente como una unidad.
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                // Si hubo un reintento, descarta lo añadido al contexto en el intento anterior
                // (los ExecuteDelete son SQL directo y no se rastrean).
                db.ChangeTracker.Clear();

                await using var tx = await db.Database.BeginTransactionAsync();

                // Lee la revisión DENTRO de la transacción, como entidad RASTREADA: la
                // comprobación de baseRevision y el incremento se apoyan en el token de
                // concurrencia de `Revision` (EF añade `WHERE Revision = @original` al
                // actualizar), de modo que dos push concurrentes del mismo usuario no puedan
                // ambos aplicarse. Con estado existente el perdedor lanza
                // DbUpdateConcurrencyException (UPDATE); en el primerísimo push de dos
                // dispositivos a la vez, la colisión de PK del INSERT (DbUpdateException) — ambos
                // casos se traducen a conflicto en los catch de abajo.
                var state = await db.UserSyncStates.FirstOrDefaultAsync(s => s.UserId == userId);
                var current = state?.Revision ?? 0;

                if (req.BaseRevision != current)
                {
                    // Conflicto: el servidor cambió desde la última sincronización del cliente.
                    logger.LogDebug("Push en conflicto para {UserId}: base {Base} != actual {Current}", userId, req.BaseRevision, current);
                    await tx.RollbackAsync();
                    var serverData = await ReadSnapshotAsync(userId);
                    return new SyncResponse { Revision = current, Conflict = true, Data = serverData };
                }

                var newRevision = current + 1;

                // Reemplazo total del snapshot del usuario.
                await db.Accounts.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Jobs.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Boosts.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.HelperStates.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Plans.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Overrides.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Deletions.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                WriteSnapshot(userId, req.Data);

                if (state is null)
                    db.UserSyncStates.Add(new UserSyncState { UserId = userId, Revision = newRevision });
                else
                    state.Revision = newRevision;

                await db.SaveChangesAsync();
                await tx.CommitAsync();

                logger.LogDebug("Push aplicado para {UserId}: revisión {Old} -> {New}", userId, current, newRevision);
                return new SyncResponse { Revision = newRevision };
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Pérdida de la carrera por el token de concurrencia de Revision (otro push del
            // mismo usuario ganó el UPDATE): se trata como conflicto para que el cliente fusione
            // y reintente (la transacción ya hizo rollback).
            logger.LogDebug(ex, "Push con conflicto de concurrencia para {UserId}", userId);
            db.ChangeTracker.Clear();
            var serverData = await ReadSnapshotAsync(userId);
            var current = await GetRevisionAsync(userId);
            return new SyncResponse { Revision = current, Conflict = true, Data = serverData };
        }
        catch (DbUpdateException ex)
        {
            // Otra DbUpdateException: el caso esperado es la colisión de PK al INSERTAR el
            // UserSyncState en dos PRIMEROS push simultáneos del mismo usuario (el token de
            // concurrencia solo cubre UPDATE, no INSERT). Distinguimos carrera de error genuino
            // SIN depender del proveedor: si la revisión del servidor avanzó respecto a la base
            // del cliente, otro push ganó → conflicto recuperable; si no, es un error real
            // (datos/BD) y propaga (500, registrado) para no enmascararlo como un 409 perpetuo.
            db.ChangeTracker.Clear();
            var serverRevision = await GetRevisionAsync(userId);
            if (serverRevision == req.BaseRevision) throw;
            logger.LogDebug(ex, "Push con colisión de escritura para {UserId}; se trata como conflicto", userId);
            var serverData = await ReadSnapshotAsync(userId);
            return new SyncResponse { Revision = serverRevision, Conflict = true, Data = serverData };
        }
    }

    private async Task<long> GetRevisionAsync(string userId) =>
        await db.UserSyncStates.Where(s => s.UserId == userId)
            .Select(s => s.Revision).FirstOrDefaultAsync();

    // ── Lectura: entidades → DTO ──────────────────────────────────────────────

    private async Task<SyncDataDto> ReadSnapshotAsync(string userId)
    {
        var accounts = await db.Accounts.Where(x => x.UserId == userId).AsNoTracking().ToListAsync();
        var jobs = await db.Jobs.Where(x => x.UserId == userId).AsNoTracking().ToListAsync();
        var boosts = await db.Boosts.Where(x => x.UserId == userId).AsNoTracking().ToListAsync();
        var helperStates = await db.HelperStates.Where(x => x.UserId == userId).AsNoTracking().ToListAsync();
        var plans = await db.Plans.Where(x => x.UserId == userId).AsNoTracking().ToListAsync();
        var overrideRow = await db.Overrides.Where(x => x.UserId == userId).AsNoTracking().FirstOrDefaultAsync();
        var deletions = await db.Deletions.Where(x => x.UserId == userId).AsNoTracking().ToListAsync();

        var dto = new SyncDataDto();
        foreach (var a in accounts)
        {
            dto.Accounts.Add(new AccountDto
            {
                Id = a.Id,
                Name = a.Name,
                Tag = a.Tag,
                Color = a.Color,
                ThLevel = a.ThLevel,
                Builders = a.Builders,
                BhLevel = a.BhLevel,
                BbBuilders = a.BbBuilders,
                GoldPass = a.GoldPass,
                PlanWindow = Deserialize<PlanWindowDto?>(a.PlanWindowJson),
                ModifiedAt = a.ModifiedAt
            });
            dto.Inventory[a.Id] = Deserialize<Dictionary<string, InventoryEntryDto>>(a.InventoryJson) ?? [];
            dto.HelperLevels[a.Id] = Deserialize<Dictionary<string, int>>(a.HelperLevelsJson) ?? [];
            if (a.MapsModifiedAt > 0) dto.AccountMapsModifiedAt[a.Id] = a.MapsModifiedAt;
        }

        foreach (var j in jobs)
            dto.Jobs.Add(new JobDto
            {
                Id = j.Id,
                AccountId = j.AccountId,
                Village = j.Village,
                ItemId = j.ItemId,
                ItemName = j.ItemName,
                ItemImage = j.ItemImage,
                Category = j.Category,
                FromLevel = j.FromLevel,
                ToLevel = j.ToLevel,
                Slot = j.Slot,
                StartedAt = j.StartedAt,
                DurationSeconds = j.DurationSeconds,
                Resource = j.Resource,
                Cost = j.Cost,
                Note = j.Note,
                Imported = j.Imported,
                HelpersApplied = Deserialize<List<HelperAppliedDto>?>(j.HelpersAppliedJson),
                ModifiedAt = j.ModifiedAt
            });

        foreach (var b in boosts)
            dto.Boosts.Add(new BoostDto
            {
                Id = b.Id,
                AccountId = b.AccountId,
                Village = b.Village,
                BoostId = b.BoostId,
                Name = b.Name,
                Multiplier = b.Multiplier,
                StartedAt = b.StartedAt,
                DurationSeconds = b.DurationSeconds,
                AppliesTo = b.AppliesTo,
                CooldownSeconds = b.CooldownSeconds,
                Imported = b.Imported,
                ModifiedAt = b.ModifiedAt
            });

        foreach (var h in helperStates)
            dto.HelperStates.Add(new HelperStateDto
            {
                Id = h.Id,
                AccountId = h.AccountId,
                HelperId = h.HelperId,
                Name = h.Name,
                StartedAt = h.StartedAt,
                CooldownSeconds = h.CooldownSeconds,
                Note = h.Note,
                ModifiedAt = h.ModifiedAt
            });

        foreach (var p in plans)
            dto.Plans[p.AccountId] = Deserialize<List<PlanItemDto>>(p.ItemsJson) ?? [];

        if (overrideRow is not null)
        {
            dto.Overrides = Deserialize<Dictionary<string, Dictionary<string, OverrideEntryDto>>>(overrideRow.Json) ?? [];
            dto.OverridesModifiedAt = overrideRow.ModifiedAt;
        }

        foreach (var d in deletions)
            dto.Deletions.Add(new TombstoneDto { Kind = d.Kind, Id = d.EntityId, ModifiedAt = d.ModifiedAt });

        return dto;
    }

    // ── Escritura: DTO → entidades (las tablas del usuario ya se vaciaron) ─────

    private void WriteSnapshot(string userId, SyncDataDto data)
    {
        // Deduplica por id las listas con clave (UserId, Id): si el cliente enviara ids
        // repetidos, una colisión de PK abortaría todo el push (igual que ya se hace con las
        // lápidas más abajo). Plans/Inventory/HelperLevels vienen indexados por clave única.
        foreach (var a in data.Accounts.GroupBy(x => x.Id).Select(g => g.First()))
        {
            db.Accounts.Add(new AccountEntity
            {
                Id = a.Id,
                UserId = userId,
                Name = a.Name,
                Tag = a.Tag,
                Color = a.Color,
                ThLevel = a.ThLevel,
                Builders = a.Builders,
                BhLevel = a.BhLevel,
                BbBuilders = a.BbBuilders,
                GoldPass = a.GoldPass,
                PlanWindowJson = a.PlanWindow is null ? null : Serialize(a.PlanWindow),
                InventoryJson = Serialize(data.Inventory.GetValueOrDefault(a.Id) ?? []),
                HelperLevelsJson = Serialize(data.HelperLevels.GetValueOrDefault(a.Id) ?? []),
                ModifiedAt = a.ModifiedAt,
                MapsModifiedAt = data.AccountMapsModifiedAt.GetValueOrDefault(a.Id)
            });
        }

        foreach (var j in data.Jobs.GroupBy(x => x.Id).Select(g => g.First()))
            db.Jobs.Add(new JobEntity
            {
                Id = j.Id,
                UserId = userId,
                AccountId = j.AccountId,
                Village = j.Village,
                ItemId = j.ItemId,
                ItemName = j.ItemName,
                ItemImage = j.ItemImage,
                Category = j.Category,
                FromLevel = j.FromLevel,
                ToLevel = j.ToLevel,
                Slot = j.Slot,
                StartedAt = j.StartedAt,
                DurationSeconds = j.DurationSeconds,
                Resource = j.Resource,
                Cost = j.Cost,
                Note = j.Note,
                Imported = j.Imported,
                HelpersAppliedJson = j.HelpersApplied is null ? null : Serialize(j.HelpersApplied),
                ModifiedAt = j.ModifiedAt
            });

        foreach (var b in data.Boosts.GroupBy(x => x.Id).Select(g => g.First()))
            db.Boosts.Add(new BoostEntity
            {
                Id = b.Id,
                UserId = userId,
                AccountId = b.AccountId,
                Village = b.Village,
                BoostId = b.BoostId,
                Name = b.Name,
                Multiplier = b.Multiplier,
                StartedAt = b.StartedAt,
                DurationSeconds = b.DurationSeconds,
                AppliesTo = b.AppliesTo,
                CooldownSeconds = b.CooldownSeconds,
                Imported = b.Imported,
                ModifiedAt = b.ModifiedAt
            });

        foreach (var h in data.HelperStates.GroupBy(x => x.Id).Select(g => g.First()))
            db.HelperStates.Add(new HelperStateEntity
            {
                Id = h.Id,
                UserId = userId,
                AccountId = h.AccountId,
                HelperId = h.HelperId,
                Name = h.Name,
                StartedAt = h.StartedAt,
                CooldownSeconds = h.CooldownSeconds,
                Note = h.Note,
                ModifiedAt = h.ModifiedAt
            });

        foreach (var (accountId, items) in data.Plans)
            db.Plans.Add(new PlanEntity { UserId = userId, AccountId = accountId, ItemsJson = Serialize(items) });

        db.Overrides.Add(new OverrideEntity { UserId = userId, Json = Serialize(data.Overrides), ModifiedAt = data.OverridesModifiedAt });

        // Deduplica por (kind, id) por si el cliente envía lápidas repetidas (la
        // PK es UserId+Kind+EntityId).
        foreach (var t in data.Deletions.GroupBy(t => (t.Kind, t.Id)).Select(g => g.First()))
            db.Deletions.Add(new DeletionEntity { UserId = userId, Kind = t.Kind, EntityId = t.Id, ModifiedAt = t.ModifiedAt });
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T? Deserialize<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);
}
