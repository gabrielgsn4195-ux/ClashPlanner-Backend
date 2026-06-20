using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ClashPlanner.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using ClashPlanner.Api.Endpoints;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
// Límite de tasa de /auth (login/registro/refresh) por IP: anti-fuerza-bruta de 2.ª
// línea (la 1.ª es el lockout por cuenta). Solo es eficaz por-cliente con la IP real,
// que recuperamos de X-Forwarded-For en producción (UseForwardedHeaders, más abajo).
int authPerMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 30);
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();

// Tope del tamaño del cuerpo de las peticiones: evita que un cliente agote memoria
// subiendo un snapshot de /sync desmesurado (la cardinalidad se valida aparte en el
// endpoint). Configurable por si un usuario con muchas cuentas necesita más margen.
builder.WebHost.ConfigureKestrel(o =>
    o.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long>("Limits:MaxRequestBodyBytes", 8L * 1024 * 1024));
// Si el probe falla, guardamos la excepción para registrarla con el logger del host
// (que aún no existe aquí). Lo normal en la 1.ª ejecución es que la tabla no exista.
Exception? settingsProbeError = null;
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
catch (Exception ex) { settingsProbeError = ex; /* normal en la 1.ª ejecución: la tabla `Settings` aún no existe */ }

// Guardrail de CORS: en producción exigimos orígenes explícitos. Sin ellos caeríamos
// en `AllowAnyOrigin()` (inseguro para una API autenticada). Fallamos rápido al arrancar,
// igual que con `Jwt:SigningKey`. En desarrollo/tests se permite cualquier origen.
if (builder.Environment.IsProduction() && corsOrigins is not { Length: > 0 })
    throw new InvalidOperationException(
        "Falta 'Cors:Origins' en producción. Configura los dominios permitidos por variable de entorno " +
        "(p. ej. Cors__Origins__0=https://midominio) o en la tabla Settings (clave Cors:Origins).");

// ── Identity (email + contraseña) ───────────────────────────────────────────
// DECISIÓN: no se exige confirmación de email (`RequireConfirmedEmail`/`Account`
// quedan en false, su valor por defecto). El email es solo un identificador de
// login: la cuenta no da acceso a nada sensible de terceros (solo sincroniza los
// datos del propio planificador) y no hay infraestructura SMTP en el despliegue
// (Render free). Si en el futuro se añade recuperación de contraseña por correo o
// el alcance crece, habría que: (1) configurar un proveedor de email, (2) activar
// `o.SignIn.RequireConfirmedEmail = true`, y (3) enviar el token de confirmación
// tras el registro en AuthEndpoints. Hasta entonces, mantenerlo simple es deliberado.
builder.Services
    .AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        // Bloqueo temporal de la cuenta ante intentos fallidos repetidos (anti-fuerza-bruta).
        // El login lleva la cuenta manualmente (AccessFailed/Reset) porque usamos
        // AddIdentityCore sin SignInManager.
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = builder.Configuration.GetValue("Identity:MaxFailedAccessAttempts", 5);
        o.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(builder.Configuration.GetValue("Identity:LockoutMinutes", 15));
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
        // Rechaza access tokens revocados (logout / logout-all): comprobación EN MEMORIA, sin
        // tocar la BD en cada petición.
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ctx =>
            {
                var principal = ctx.Principal;
                if (principal is not null)
                {
                    var rev = ctx.HttpContext.RequestServices.GetRequiredService<TokenRevocationService>();
                    var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
                    var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                    var iat = principal.FindFirstValue(JwtRegisteredClaimNames.Iat);
                    // Sin iat (no debería faltar): trátalo como muy antiguo → fail-closed ante epoch.
                    var issuedAt = iat is not null && long.TryParse(iat, out var unix)
                        ? DateTimeOffset.FromUnixTimeSeconds(unix)
                        : DateTimeOffset.MinValue;
                    if (rev.IsRevoked(jti, sub, issuedAt)) ctx.Fail("Token revocado.");
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ── Servicios de la aplicación ──────────────────────────────────────────────
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<CocService>();
builder.Services.AddScoped<AppSettingsService>();
// Purga periódica de refresh tokens muertos (en segundo plano).
builder.Services.AddHostedService<RefreshTokenCleanupService>();
builder.Services.AddMemoryCache();
// Revocación de access tokens en memoria (logout / logout-all) sin tocar la BD por petición.
builder.Services.AddSingleton<TokenRevocationService>();
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
    // Límite por IP de los endpoints de autenticación (login/registro/refresh).
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = authPerMinute, QueueLimit = 0 }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Respuestas de error uniformes (ProblemDetails) para el manejador global de excepciones.
builder.Services.AddProblemDetails();

var app = builder.Build();

// Ya hay logger del host: registra (en Debug, sin ruido) el fallo del probe de Settings.
if (settingsProbeError is not null)
    app.Logger.LogDebug(settingsProbeError,
        "No se pudo leer la tabla Settings al arrancar (normal en la 1.ª ejecución, antes de migrar).");

// Arranque: aplica las migraciones SOLO cuando «Database:Migrate» está activo
// (cómodo en dev; en producción conviene ejecutarlas como paso del despliegue).
// Los tests lo desactivan y crean el esquema con EnsureCreated. La siembra de
// roles y de la configuración por defecto se ejecuta SIEMPRE: no puede depender
// de la migración, porque el registro de usuarios asigna el rol «Usuario» y
// fallaría (también en los tests) si el rol no existiera.
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;

    if (builder.Configuration.GetValue("Database:Migrate", true))
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
    await settings.SeedAsync(SettingKeys.CocProxyUrl, SettingKeys.DefaultCocProxyUrl);
    await settings.SeedAsync(SettingKeys.CocDirectUrl, SettingKeys.DefaultCocDirectUrl);
    await settings.SeedAsync(SettingKeys.CocTimeoutSeconds, "15");
    await settings.SeedAsync(SettingKeys.RateLimitCocPerMinute, "30");
    await settings.SeedAsync(SettingKeys.CorsOrigins, string.Join(',', builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []));
    await settings.SeedAsync(SettingKeys.CocToken, builder.Configuration["Coc:Token"]);
}

// ── Pipeline HTTP ────────────────────────────────────────────────────────────
// Cabeceras de seguridad para TODAS las respuestas. Se fijan vía Response.OnStarting
// (no inline) para que SOBREVIVAN al Response.Clear() del manejador global de
// excepciones y se emitan también en las respuestas de error (500). Este middleware
// va el PRIMERO, aguas arriba del exception handler. HSTS solo sobre HTTPS y fuera de
// Development (el TLS lo termina el proxy; el esquema real llega por X-Forwarded-Proto,
// resuelto por UseForwardedHeaders, que corre antes de que dispare este callback).
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        if (!app.Environment.IsDevelopment() && ctx.Request.IsHttps)
            h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        return Task.CompletedTask;
    });
    await next();
});

// Fuera de desarrollo (Producción y Testing) endurecemos el pipeline. En Development
// se conserva la página de error detallada del host y Swagger.
if (!app.Environment.IsDevelopment())
{
    // Detrás del proxy de Render (que termina TLS) recuperamos esquema/IP reales del
    // cliente desde X-Forwarded-*. Sin esto, el rate limiter por IP agruparía TODO el
    // tráfico bajo la IP del proxy y el esquema HTTPS no se detectaría. El proxy no tiene
    // IP fija conocida → aceptamos las cabeceras de cualquier origen ascendente.
    var fwd = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    fwd.KnownIPNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);

    // Manejador global de excepciones: respuesta ProblemDetails uniforme, SIN filtrar
    // trazas internas al cliente.
    app.UseExceptionHandler();
}
else
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
app.MapEventsEndpoints();

app.Run();

/// <summary>Clase parcial expuesta para los tests de integración (WebApplicationFactory).</summary>
public partial class Program { }
