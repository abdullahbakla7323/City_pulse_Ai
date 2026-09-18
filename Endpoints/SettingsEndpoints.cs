using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using CityPulseAI.Domain;
using CityPulseAI.Services.Hubs;

namespace CityPulseAI.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/settings");

        // Get autopilot status
        group.MapGet("/autopilot", () => Results.Ok(new { enabled = SystemSettings.AutoPilot }));

        // Toggle autopilot status
        group.MapPost("/autopilot", async (AutopilotRequest req, IHubContext<CityPulseHub> hub) =>
        {
            SystemSettings.AutoPilot = req.Enabled;
            
            // Broadcast setting change to all clients
            await hub.Clients.All.SendAsync("AutopilotToggled", new { enabled = SystemSettings.AutoPilot });
            
            return Results.Ok(new { enabled = SystemSettings.AutoPilot });
        });
    }
}

public record AutopilotRequest(bool Enabled);
