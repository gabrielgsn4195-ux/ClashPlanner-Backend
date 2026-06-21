using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClashPlanner.Api.Data;
using ClashPlanner.Api.Dtos;
using ClashPlanner.Api.Models;
using ClashPlanner.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClashPlanner.Api.Endpoints;

/// <summary>
/// Endpoints de autenticación (email + contraseña) con JWT y refresh tokens
/// rotatorios: registro, login, refresco y logout.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Hash dummy (formato Identity v3) precomputado una vez. En el login SIN usuario se
    /// verifica contra él para igualar el coste de hashing y no filtrar por TIMING si un
    /// email está registrado (el camino con usuario ejecuta CheckPasswordAsync). Ver F-023.
    /// </summary>
    private static readonly string DummyPasswordHash =
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "timing-equalizer");

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Rate limit por IP en todo /auth (login/registro/refresh): anti-fuerza-bruta
        // de 2.ª línea, complementa el lockout por cuenta del login.
        var g = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth");

        // Alta de usuario; si tiene éxito, inicia sesión automáticamente.
        g.MapPost("/register", async (
            RegisterRequest req, UserManager<ApplicationUser> users, TokenService tokens, AppDbContext db) =>
        {
            var user = new ApplicationUser { UserName = req.Email, Email = req.Email };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                // Anti-enumeración: NO revelar si el email ya está registrado. Los errores
                // «Duplicate*» delatarían cuentas existentes → respuesta genérica. El resto
                // (p. ej. reglas de contraseña) hablan del input enviado, no de la existencia
                // de la cuenta, así que sí se devuelven al cliente.
                // (Anti-enumeración completa requeriría confirmación de email — pendiente, Oleada 2.)
                if (result.Errors.Any(e => e.Code.StartsWith("Duplicate", StringComparison.Ordinal)))
                    // `reason` estable e independiente del idioma para que el cliente muestre un
                    // mensaje neutro útil (sin revelar que el email ya existe).
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "No se pudo completar el registro.",
                        extensions: new Dictionary<string, object?> { ["reason"] = "register-failed" });
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            }
            // Todo registro nuevo entra como «Usuario»; Admin/Técnico se asignan aparte.
            await users.AddToRoleAsync(user, Roles.Usuario);
            return Results.Ok(await IssueAsync(user, tokens, db, users));
        });

        // Inicio de sesión: valida credenciales y emite tokens.
        g.MapPost("/login", async (
            LoginRequest req, UserManager<ApplicationUser> users, TokenService tokens, AppDbContext db) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            // 401 uniforme tanto si el usuario no existe como si la contraseña es incorrecta
            // o la cuenta está bloqueada: no revelamos cuál de los tres es (anti-enumeración).
            if (user is null)
            {
                // Iguala el coste DOMINANTE (el hashing PBKDF2) aunque el usuario NO exista,
                // para no filtrar por timing si un email está registrado. Queda un diferencial
                // residual menor (el camino con usuario hace además IsLockedOutAsync y escrituras
                // de fallo) que el hashing domina con holgura; riesgo aceptado. Ver F-023.
                users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, req.Password);
                return Results.Unauthorized();
            }
            if (await users.IsLockedOutAsync(user)) return Results.Unauthorized();
            if (!await users.CheckPasswordAsync(user, req.Password))
            {
                // Cuenta el intento fallido; al llegar al umbral, Identity bloquea la cuenta
                // temporalmente (anti-fuerza-bruta).
                await users.AccessFailedAsync(user);
                return Results.Unauthorized();
            }
            // Login correcto: reinicia el contador de fallos.
            await users.ResetAccessFailedCountAsync(user);
            return Results.Ok(await IssueAsync(user, tokens, db, users));
        });

        // Refresco: rota el refresh token (revoca el usado, emite uno nuevo de la misma familia).
        g.MapPost("/refresh", async (
            RefreshRequest req, UserManager<ApplicationUser> users, TokenService tokens, AppDbContext db) =>
        {
            var hash = TokenService.HashToken(req.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hash);
            if (stored is null) return Results.Unauthorized();

            var now = DateTime.UtcNow;

            // Reuso de un token YA revocado (rotado o cerrado y presentado de nuevo).
            if (stored.RevokedAt is not null)
            {
                // Ventana de gracia: si se acaba de rotar (reintento benigno / doble envío de la
                // misma petición), NO disparamos la cascada —que mataría la sesión sucesora
                // activa—, solo rechazamos. Pasada la ventana, el reuso de un token revocado se
                // trata como robo → revoca toda la familia (o, para tokens antiguos sin familia,
                // todas las sesiones del usuario).
                if (now - stored.RevokedAt.Value > tokens.RefreshReuseGrace)
                {
                    var family = db.RefreshTokens.Where(t => t.RevokedAt == null);
                    family = stored.FamilyId is Guid fam
                        ? family.Where(t => t.FamilyId == fam)
                        : family.Where(t => t.UserId == stored.UserId);
                    await family.ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
                }
                return Results.Unauthorized();
            }

            // Expirado por inactividad o por deadline absoluto de la sesión.
            if (!stored.IsActive) return Results.Unauthorized();

            // Revoca el token de forma ATÓMICA y SOLO si sigue activo: si dos refrescos
            // concurrentes usan el mismo token, solo uno revoca (1 fila afectada) y rota; el
            // otro ve 0 filas y se rechaza, preservando el uso único bajo concurrencia.
            var rotated = await db.RefreshTokens
                .Where(t => t.Id == stored.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
            if (rotated == 0) return Results.Unauthorized();

            var user = await users.FindByIdAsync(stored.UserId);
            if (user is null) return Results.Unauthorized();
            // Conserva la familia y el deadline absoluto al rotar.
            return Results.Ok(await IssueAsync(user, tokens, db, users, rotateFrom: stored));
        });

        // Cierre de sesión: revoca el refresh token indicado e invalida el access token actual.
        g.MapPost("/logout", async (
            LogoutRequest req, AppDbContext db, ClaimsPrincipal principal,
            TokenRevocationService revocation, TokenService tokens) =>
        {
            var hash = TokenService.HashToken(req.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hash);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            // Si el cliente manda su access token (cabecera Authorization), lo invalidamos ya;
            // si no, expira solo en ≤ la vida del access token.
            var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (jti is not null)
                revocation.RevokeJti(jti, DateTimeOffset.UtcNow.AddSeconds(tokens.AccessTokenSeconds));
            return Results.NoContent();
        });

        // Cierre de sesión en TODOS los dispositivos: revoca todos los refresh tokens activos
        // del usuario e invalida todos sus access tokens (también útil ante sospecha de compromiso).
        g.MapPost("/logout-all", async (
            ClaimsPrincipal principal, AppDbContext db,
            TokenRevocationService revocation, TokenService tokens) =>
        {
            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                         ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null) return Results.Unauthorized();
            await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
            var until = DateTimeOffset.UtcNow.AddSeconds(tokens.AccessTokenSeconds);
            // El access token actual se invalida de forma exacta por su jti; el resto de tokens
            // del usuario, por epoch (los emitidos hasta ahora dejan de valer).
            var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (jti is not null) revocation.RevokeJti(jti, until);
            revocation.RevokeAllForUser(userId, until);
            return Results.NoContent();
        })
            .RequireAuthorization();

        // Datos del usuario autenticado (sonda de sesión): email + roles.
        g.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new
            {
                email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
                roles = user.FindAll("role").Select(c => c.Value).ToArray()
            }))
            .RequireAuthorization();
    }

    /// <summary>
    /// Emite access + refresh token (con roles) y persiste el refresh token. Si
    /// <paramref name="rotateFrom"/> se indica (refresco), el nuevo token hereda su familia
    /// y su deadline absoluto; si no (login/registro), abre una familia y deadline nuevos.
    /// </summary>
    private static async Task<AuthResponse> IssueAsync(
        ApplicationUser user, TokenService tokens, AppDbContext db, UserManager<ApplicationUser> users,
        RefreshToken? rotateFrom = null)
    {
        var roles = await users.GetRolesAsync(user);
        var (refresh, plaintext) = rotateFrom is null
            ? tokens.CreateRefreshToken(user.Id)
            : tokens.CreateRefreshToken(user.Id, rotateFrom.FamilyId, rotateFrom.AbsoluteExpiresAt);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync();
        return new AuthResponse
        {
            AccessToken = tokens.CreateAccessToken(user, roles),
            RefreshToken = plaintext, // el cliente recibe el valor en claro; en BD queda el hash
            ExpiresInSeconds = tokens.AccessTokenSeconds,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToArray()
        };
    }
}
