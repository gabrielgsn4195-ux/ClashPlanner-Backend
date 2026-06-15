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
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth").WithTags("Auth");

        // Alta de usuario; si tiene éxito, inicia sesión automáticamente.
        g.MapPost("/register", async (
            RegisterRequest req, UserManager<ApplicationUser> users, TokenService tokens, AppDbContext db) =>
        {
            var user = new ApplicationUser { UserName = req.Email, Email = req.Email };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.ValidationProblem(result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
            // Todo registro nuevo entra como «Usuario»; Admin/Técnico se asignan aparte.
            await users.AddToRoleAsync(user, Roles.Usuario);
            return Results.Ok(await IssueAsync(user, tokens, db, users));
        });

        // Inicio de sesión: valida credenciales y emite tokens.
        g.MapPost("/login", async (
            LoginRequest req, UserManager<ApplicationUser> users, TokenService tokens, AppDbContext db) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null || !await users.CheckPasswordAsync(user, req.Password))
                return Results.Unauthorized();
            return Results.Ok(await IssueAsync(user, tokens, db, users));
        });

        // Refresco: rota el refresh token (revoca el usado, emite uno nuevo).
        g.MapPost("/refresh", async (
            RefreshRequest req, UserManager<ApplicationUser> users, TokenService tokens, AppDbContext db) =>
        {
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == req.RefreshToken);
            if (stored is null || !stored.IsActive) return Results.Unauthorized();
            stored.RevokedAt = DateTime.UtcNow;
            var user = await users.FindByIdAsync(stored.UserId);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(await IssueAsync(user, tokens, db, users));
        });

        // Cierre de sesión: revoca el refresh token indicado.
        g.MapPost("/logout", async (LogoutRequest req, AppDbContext db) =>
        {
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == req.RefreshToken);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        // Datos del usuario autenticado (sonda de sesión): email + roles.
        g.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new
            {
                email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email"),
                roles = user.FindAll("role").Select(c => c.Value).ToArray()
            }))
            .RequireAuthorization();
    }

    /// <summary>Emite access + refresh token (con roles) y persiste el refresh token.</summary>
    private static async Task<AuthResponse> IssueAsync(
        ApplicationUser user, TokenService tokens, AppDbContext db, UserManager<ApplicationUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        var refresh = tokens.CreateRefreshToken(user.Id);
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync();
        return new AuthResponse
        {
            AccessToken = tokens.CreateAccessToken(user, roles),
            RefreshToken = refresh.Token,
            ExpiresInSeconds = tokens.AccessTokenSeconds,
            Email = user.Email ?? string.Empty,
            Roles = roles.ToArray()
        };
    }
}
