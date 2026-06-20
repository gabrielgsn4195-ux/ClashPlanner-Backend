using ClashPlanner.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ClashPlanner.Api.Services;

/// <summary>
/// Purga periódica de refresh tokens muertos para que la tabla no crezca sin límite:
/// borra los revocados hace más de <see cref="RevokedRetentionDays"/> días (se conservan
/// un tiempo para que la detección de reuso pueda seguir disparando) y los no revocados
/// que ya caducaron (por inactividad o por deadline absoluto). Corre cada
/// <see cref="IntervalHours"/> horas en segundo plano.
/// </summary>
public sealed class RefreshTokenCleanupService(
    IServiceProvider services,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    private const int IntervalHours = 12;

    /// <summary>Días que se conservan los tokens revocados antes de purgarlos.</summary>
    public const int RevokedRetentionDays = 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera primero (no corre durante el arranque ni en los tests, que terminan antes).
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(IntervalHours), stoppingToken);
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await PurgeAsync(db, DateTime.UtcNow, stoppingToken);
                if (deleted > 0) logger.LogInformation("Purga de refresh tokens: {Count} eliminados.", deleted);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                logger.LogWarning(e, "Fallo en la purga de refresh tokens; se reintenta en el próximo ciclo.");
            }
        }
    }

    /// <summary>
    /// Borra los refresh tokens muertos y devuelve cuántos se eliminaron. Conserva los
    /// revocados recientemente (&lt; <see cref="RevokedRetentionDays"/> días) y los activos.
    /// </summary>
    public static async Task<int> PurgeAsync(AppDbContext db, DateTime now, CancellationToken ct = default)
    {
        var revokedCutoff = now.AddDays(-RevokedRetentionDays);
        return await db.RefreshTokens
            .Where(t =>
                (t.RevokedAt != null && t.RevokedAt < revokedCutoff)
                || (t.RevokedAt == null
                    && (t.ExpiresAt < now || (t.AbsoluteExpiresAt != null && t.AbsoluteExpiresAt < now))))
            .ExecuteDeleteAsync(ct);
    }
}
