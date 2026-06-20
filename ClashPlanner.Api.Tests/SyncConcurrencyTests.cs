using ClashPlanner.Api.Data;
using ClashPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClashPlanner.Api.Tests;

/// <summary>
/// Test del token de concurrencia (O1) de <c>UserSyncState.Revision</c>: dos escrituras que
/// parten de la MISMA revisión no pueden ambas aplicarse (anti-pérdida de actualización). El
/// arnés usa SQLite con una conexión única (que serializa el acceso y no reproduce la carrera
/// vía HTTP), así que se ejercita el token directamente con dos <c>DbContext</c> sobre la misma
/// base. Es el mecanismo en el que se apoya <c>SyncService.PushAsync</c> para devolver conflicto
/// en vez de pisar datos en pushes concurrentes.
/// </summary>
public class SyncConcurrencyTests
{
    [Fact]
    public async Task El_token_de_concurrencia_de_Revision_impide_la_perdida_de_actualizacion()
    {
        using var f = new ApiFactory();
        using (var seed = f.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserSyncStates.Add(new UserSyncState { UserId = "u1", Revision = 5 });
            await db.SaveChangesAsync();
        }

        // Dos contextos sobre la misma BD leen la MISMA revisión (5) como entidad rastreada.
        using var s1 = f.Services.CreateScope();
        using var s2 = f.Services.CreateScope();
        var db1 = s1.ServiceProvider.GetRequiredService<AppDbContext>();
        var db2 = s2.ServiceProvider.GetRequiredService<AppDbContext>();
        var st1 = await db1.UserSyncStates.FirstAsync(s => s.UserId == "u1");
        var st2 = await db2.UserSyncStates.FirstAsync(s => s.UserId == "u1");

        // El primero aplica 5 → 6.
        st1.Revision = 6;
        await db1.SaveChangesAsync();

        // El segundo, que leyó 5, intenta también 5 → 6: el token añade `WHERE Revision = 5`,
        // que ahora afecta 0 filas → DbUpdateConcurrencyException (sin el token sería pérdida
        // de actualización: el segundo pisaría al primero).
        st2.Revision = 6;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }
}
