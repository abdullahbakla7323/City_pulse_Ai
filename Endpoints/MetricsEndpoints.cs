using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Domain;

namespace CityPulseAI.Endpoints;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/system/metrics", async (AppDbContext db) =>
        {
            int activeIncidents = await db.Incidents.CountAsync(i => i.Status != "Resolved");
            int resolvedIncidents = await db.Incidents.CountAsync(i => i.Status == "Resolved");
            int idleCrews = await db.Crews.CountAsync(c => c.Status == CrewStatus.Idle);
            
            decimal totalBudget = await db.WorkOrders
                .Where(w => w.Status == "Completed")
                .SumAsync(w => w.BudgetSpent);

            return Results.Ok(new
            {
                activeIncidents,
                resolvedIncidents,
                idleCrews,
                totalBudgetSpent = totalBudget
            });
        });
    }
}
