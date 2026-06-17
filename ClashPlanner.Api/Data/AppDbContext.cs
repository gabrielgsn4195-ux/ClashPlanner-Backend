using ClashPlanner.Api.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClashPlanner.Api.Data;

/// <summary>
/// Contexto de EF Core: tablas de Identity (usuarios/roles) más las entidades de
/// sincronización del planificador, los refresh tokens y, opcionalmente, las
/// claves de Data Protection (para compartirlas entre instancias). Las entidades
/// de sync tienen clave compuesta (id de cliente + UserId) y un índice por
/// UserId para las operaciones de pull/push, que siempre filtran por usuario.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Claves de Data Protection (cuando se persisten en la BD). </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<BoostEntity> Boosts => Set<BoostEntity>();
    public DbSet<HelperStateEntity> HelperStates => Set<HelperStateEntity>();
    public DbSet<PlanEntity> Plans => Set<PlanEntity>();
    public DbSet<OverrideEntity> Overrides => Set<OverrideEntity>();
    public DbSet<UserSyncState> UserSyncStates => Set<UserSyncState>();
    public DbSet<DeletionEntity> Deletions => Set<DeletionEntity>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Nombres descriptivos para las tablas de Identity (por defecto `AspNet*`).
        b.Entity<ApplicationUser>().ToTable("Users");
        b.Entity<IdentityRole>().ToTable("Roles");
        b.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        b.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        b.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        b.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");
        b.Entity<IdentityUserToken<string>>().ToTable("UserTokens");

        b.Entity<AccountEntity>(e =>
        {
            // La «cuenta de juego» se llama Aldea en el producto → tabla `Villages`.
            e.ToTable("Villages");
            e.HasKey(x => new { x.UserId, x.Id });
            e.HasIndex(x => x.UserId);
        });
        b.Entity<JobEntity>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Id });
            e.HasIndex(x => x.UserId);
        });
        b.Entity<BoostEntity>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Id });
            e.HasIndex(x => x.UserId);
        });
        b.Entity<HelperStateEntity>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Id });
            e.HasIndex(x => x.UserId);
        });
        b.Entity<PlanEntity>(e => e.HasKey(x => new { x.UserId, x.AccountId }));
        b.Entity<OverrideEntity>(e => e.HasKey(x => x.UserId));
        b.Entity<UserSyncState>(e => e.HasKey(x => x.UserId));
        b.Entity<DeletionEntity>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Kind, x.EntityId });
            e.HasIndex(x => x.UserId);
        });
        b.Entity<RefreshToken>(e => e.HasIndex(x => x.Token).IsUnique());
        // Configuración general de la app (clave/valor). Separada de los datos de
        // usuario y de los «valores de mejora» (catálogo).
        b.Entity<AppSetting>(e =>
        {
            e.ToTable("Settings");
            e.HasKey(x => x.Key);
        });

        // System-versioning (Tablas Temporales) en 15 tablas: historial en el esquema
        // `history.<Tabla>` con columnas de periodo ValidFrom/ValidTo. Provisionado en la
        // BD fuera de banda (script SQL); aquí se declara para que el modelo de EF coincida
        // y las migraciones futuras sepan desactivar/reactivar el versionado al alterarlas.
        //
        // SOLO para SQL Server: el versionado no existe en SQLite, que es el proveedor de
        // los tests de integración (EnsureCreated en memoria). Sin este guard, las pruebas
        // romperían. Excluidas a propósito: Settings, Roles, DataProtectionKeys y la propia
        // __EFMigrationsHistory.
        if (Database.IsSqlServer())
        {
            // Recibe el EntityTypeBuilder NO genérico para evitar la ambigüedad entre las
            // sobrecargas genérica/no genérica de ToTable(name, Action<TableBuilder>).
            void Temporal(EntityTypeBuilder e, string table) => e.ToTable(table, t => t.IsTemporal(tt =>
            {
                tt.UseHistoryTable(table, "history");
                tt.HasPeriodStart("ValidFrom");
                tt.HasPeriodEnd("ValidTo");
            }));

            // Sync/negocio (9).
            Temporal(b.Entity<AccountEntity>(), "Villages");
            Temporal(b.Entity<JobEntity>(), "Jobs");
            Temporal(b.Entity<BoostEntity>(), "Boosts");
            Temporal(b.Entity<HelperStateEntity>(), "HelperStates");
            Temporal(b.Entity<PlanEntity>(), "Plans");
            Temporal(b.Entity<OverrideEntity>(), "Overrides");
            Temporal(b.Entity<UserSyncState>(), "UserSyncStates");
            Temporal(b.Entity<DeletionEntity>(), "Deletions");
            Temporal(b.Entity<RefreshToken>(), "RefreshTokens");

            // Identity (6) — Roles queda excluida a propósito.
            Temporal(b.Entity<ApplicationUser>(), "Users");
            Temporal(b.Entity<IdentityUserRole<string>>(), "UserRoles");
            Temporal(b.Entity<IdentityUserClaim<string>>(), "UserClaims");
            Temporal(b.Entity<IdentityUserLogin<string>>(), "UserLogins");
            Temporal(b.Entity<IdentityRoleClaim<string>>(), "RoleClaims");
            Temporal(b.Entity<IdentityUserToken<string>>(), "UserTokens");
        }
    }
}
