using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;

namespace CityPulseAI.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        // User registration
        group.MapPost("/register", async (RegisterRequest request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Username and password cannot be empty." });
            }

            var exists = await db.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (exists)
            {
                return Results.BadRequest(new { error = "This username is already taken." });
            }

            var user = new User
            {
                Username = request.Username,
                Password = request.Password, // Plaintext simplicity for demo project
                Role = request.Role
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Ok(new { success = true, username = user.Username, role = user.Role.ToString() });
        });

        // User login
        group.MapPost("/login", async (LoginRequest request, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Username and password must be provided." });
            }

            try
            {
                var user = await db.Users.FirstOrDefaultAsync(u => 
                    u.Username.ToLower() == request.Username.ToLower() && u.Password == request.Password);
                    
                if (user == null)
                {
                    return Results.BadRequest(new { error = "Invalid username or password." });
                }

                return Results.Ok(new { success = true, username = user.Username, role = user.Role.ToString() });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: $"Database connection error: {ex.Message}. Please ensure PostgreSQL is running and DATABASE_URL is configured.",
                    statusCode: 500
                );
            }
        });
    }
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Citizen;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
