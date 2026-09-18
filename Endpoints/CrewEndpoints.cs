using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using CityPulseAI.Infrastructure.Data;

namespace CityPulseAI.Endpoints;

public static class CrewEndpoints
{
    public static void MapCrewEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/crews");

        // Get all crews with details
        group.MapGet("/", async (AppDbContext db) =>
            await db.Crews
                .Select(c => new {
                    c.Id,
                    c.Name,
                    type = c.Type.ToString(),
                    status = c.Status.ToString(),
                    lat = c.Location.Y,
                    lng = c.Location.X
                })
                .ToListAsync());
    }
}
