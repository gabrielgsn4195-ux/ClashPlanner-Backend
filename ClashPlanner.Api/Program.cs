using System.Text;
using System.Threading.RateLimiting;
using ClashPlanner.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using ClashPlanner.Api.Endpoints;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Configuración de tokens ─────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException(
        "Falta 'Jwt:SigningKey' (≥32 caracteres). Configúralo en appsettings.Development.json (dev) " +
        "o por variable de entorno / user-secrets (producción).");

// ── Base de datos (SQL Server) ──────────────────────────────────────────────
var conn = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'DefaultConnection'.");
// `EnableRetryOnFailure` cubre los fallos transitorios de conexión (p. ej. al
// arrancar antes de que SQL Server del contenedor esté listo).
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(conn, sql => sql.EnableRetryOnFailure()));

// Ajustes que se aplican AL ARRANCAR (rate-limit del proxy y orígenes CORS): se
// leen de la tabla `Settings` si ya existe; si no (primera ejecución), se usan
// los de config y luego se siembran. Cambiarlos requiere reiniciar el servidor.
int cocPerMinute = builder.Configuration.GetValue("RateLimit:CocPerMinute", 30);
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
try
{
    var probeOpts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(conn).Options;
    using var probe = new AppDbContext(probeOpts);
    foreach (var r in probe.Settings.AsNoTracking()
        .Where(s => s.Key == SettingKeys.RateLimitCocPerMinute || s.Key == SettingKeys.CorsOrigins).ToList())
    {
        if (r.Key == SettingKeys.RateLimitCocPerMinute && int.TryParse(r.Value, out var n)) cocPerMinute = n;
        else if (r.Key == SettingKeys.CorsOrigins && !string.IsNullOrWhiteSpace(r.Value))
            corsOrigins = r.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
catch { /* primera ejecución: la tabla `Settings` aún no existe */ }

// ── Identity (email + contraseña) ───────────────────────────────────────────
builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// ── Autenticación JWT ───────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // conserva el claim `sub` tal cual
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            // El token emite los roles con el claim "role" (ver TokenService).
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

// ── Servicios de la aplicación ──────────────────────────────────────────────
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<CocService>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
// Data Protection cifra el token de CoC en reposo. Las claves DEBEN persistir
// entre reinicios (si no, los tokens guardados dejan de poder descifrarse): en
// contenedor se monta un volumen y se configura `DataProtection:KeysPath`. En
// desarrollo (sin ruta) usa el almacén por defecto del SO.
var dp = builder.Services.AddDataProtection().SetApplicationName("ClashPlanner");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.Equals(builder.Configuration["DataProtection:Store"], "Database", StringComparison.OrdinalIgnoreCase))
    // Varias instancias comparten las claves a través de la BD.
    dp.PersistKeysToDbContext<AppDbContext>();
else if (!string.IsNullOrWhiteSpace(keysPath))
    // Una sola instancia: claves en un volumen de disco.
    dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// CORS: la autenticación va por cabecera Authorization (sin cookies), así que
// permitimos cualquier origen en desarrollo. En producción, restringir a los
// dominios de la web/app vía el ajuste «Cors:Origins» (resuelto arriba).
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins is { Length: > 0 }) p.WithOrigins(corsOrigins);
    else p.AllowAnyOrigin();
    p.AllowAnyHeader().AllowAnyMethod();
}));

// Límite de tasa del proxy de CoC (abierto, sin sesión): por IP, ventana fija.
// Protege el único token de servidor de un uso abusivo de la cuota de la API.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("coc", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = cocPerMinute, QueueLimit = 0 }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Aplica las migraciones pendientes al arrancar (cómodo en dev; en producción
// conviene ejecutarlas como paso del despliegue). Los tests lo desactivan
// («Database:Migrate» = false) porque crean el esquema con otro proveedor.
if (builder.Configuration.GetValue("Database:Migrate", true))
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync();

    // Siembra los roles (Admin/Técnico/Usuario) si faltan.
    var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in Roles.All)
        if (!await roleMgr.RoleExistsAsync(role))
            await roleMgr.CreateAsync(new IdentityRole(role));

    // Siembra la configuración general por defecto (solo si la clave no existe).
    // El token se importa del entorno/fichero actual (`Coc:Token`) la 1ª vez.
    var settings = sp.GetRequiredService<AppSettingsService>();
    await settings.SeedAsync(SettingKeys.CocUseProxy, "true");
    await settings.SeedAsync(SettingKeys.CocProxyUrl, "https://cocproxy.royaleapi.dev/v1");
    await settings.SeedAsync(SettingKeys.CocDirectUrl, "https://api.clashofclans.com/v1");
    await settings.SeedAsync(SettingKeys.CocTimeoutSeconds, "15");
    await settings.SeedAsync(SettingKeys.RateLimitCocPerMinute, "30");
    await settings.SeedAsync(SettingKeys.CorsOrigins, string.Join(',', builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []));
    await settings.SeedAsync(SettingKeys.CocToken, builder.Configuration["Coc:Token"]);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");
app.MapAuthEndpoints();
app.MapSyncEndpoints();
app.MapCocEndpoints();
app.MapAdminEndpoints();

app.Run();

/// <summary>Clase parcial expuesta para los tests de integración (WebApplicationFactory).</summary>
public partial class Program { }
