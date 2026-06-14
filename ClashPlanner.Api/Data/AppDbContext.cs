using ClashPlanner.Api.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<CocTokenEntity> CocTokens => Set<CocTokenEntity>();

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
        b.Entity<CocTokenEntity>(e => e.HasKey(x => x.UserId));
        b.Entity<RefreshToken>(e => e.HasIndex(x => x.Token).IsUnique());
    }
}
