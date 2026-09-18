using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Services.Hubs;

namespace CityPulseAI.Services.Maps;

public class CrewSimulationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<CityPulseHub> _hubContext;
    private readonly ILogger<CrewSimulationService> _logger;

    public CrewSimulationService(
        IServiceProvider serviceProvider,
        IHubContext<CityPulseHub> hubContext,
        ILogger<CrewSimulationService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Crew Simulation Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Fetch work orders where status is Assigned (Crew is on the way)
                var activeOrders = await dbContext.WorkOrders
                    .Include(w => w.Crew)
                    .Include(w => w.Incident)
                    .Where(w => w.Status == "Assigned")
                    .ToListAsync(stoppingToken);

                foreach (var order in activeOrders)
                {
                    var crew = order.Crew;
                    var incident = order.Incident;

                    if (crew == null || incident == null) continue;

                    double crewLng = crew.Location.X;
                    double crewLat = crew.Location.Y;
                    double incidentLng = incident.Location.X;
                    double incidentLat = incident.Location.Y;

                    // Compute vector distance
                    double deltaLng = incidentLng - crewLng;
                    double deltaLat = incidentLat - crewLat;
                    double distance = Math.Sqrt(deltaLng * deltaLng + deltaLat * deltaLat);

                    // Move step speed in coordinates per tick (about ~50 meters)
                    double step = 0.0007;

                    if (distance <= step)
                    {
                        // Crew has arrived at the location
                        crew.Location = incident.Location;
                        crew.Status = CrewStatus.Busy;
                        order.Status = "InProgress"; // Changed to working state
                        
                        _logger.LogInformation($"Crew {crew.Name} arrived at Incident: {incident.Title}");

                        // Notify clients that the crew has arrived
                        await _hubContext.Clients.All.SendAsync("CrewArrived", new {
                            crewId = crew.Id,
                            crewName = crew.Name,
                            incidentId = incident.Id,
                            incidentTitle = incident.Title
                        }, stoppingToken);

                        // Trigger work completion after a simulated delay (e.g. 7 seconds)
                        _ = CompleteWorkOrderAfterDelayAsync(crew.Id, order.Id, incident.Id);
                    }
                    else
                    {
                        // Manhattan routing simulation (moves along grid streets/axes)
                        double newLng = crewLng;
                        double newLat = crewLat;

                        if (Math.Abs(deltaLat) > 0.00005)
                        {
                            double move = Math.Sign(deltaLat) * step;
                            if (Math.Abs(deltaLat) <= step)
                            {
                                newLat = incidentLat;
                            }
                            else
                            {
                                newLat = crewLat + move;
                            }
                        }
                        else
                        {
                            double move = Math.Sign(deltaLng) * step;
                            if (Math.Abs(deltaLng) <= step)
                            {
                                newLng = incidentLng;
                            }
                            else
                            {
                                newLng = crewLng + move;
                            }
                        }

                        var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
                        crew.Location = geometryFactory.CreatePoint(new Coordinate(newLng, newLat));
                        crew.Status = CrewStatus.OnWay;

                        _logger.LogDebug($"Moving Crew {crew.Name} to coordinates ({newLat}, {newLng}) via grid road");
                    }

                    await dbContext.SaveChangesAsync(stoppingToken);

                    int? targetIncidentId = null;
                    string? targetIncidentTitle = null;
                    int? distanceMeters = null;

                    if (incident != null && (crew.Status == CrewStatus.OnWay || crew.Status == CrewStatus.Busy))
                    {
                        targetIncidentId = incident.Id;
                        targetIncidentTitle = incident.Title;
                        distanceMeters = (int)CalculateDistanceInMeters(crew.Location.Y, crew.Location.X, incident.Location.Y, incident.Location.X);
                    }

                    // Broadcast the updated crew location to maps
                    await _hubContext.Clients.All.SendAsync("CrewMoved", new {
                        crewId = crew.Id,
                        crewName = crew.Name,
                        lat = crew.Location.Y,
                        lng = crew.Location.X,
                        status = crew.Status.ToString(),
                        type = crew.Type.ToString(),
                        targetIncidentId,
                        targetIncidentTitle,
                        distanceMeters
                    }, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Crew Simulation Tick");
            }

            // Tick simulation every 1.5 seconds
            await Task.Delay(1500, stoppingToken);
        }
    }

    private async Task CompleteWorkOrderAfterDelayAsync(int crewId, int orderId, int incidentId)
    {
        // Simulate repair duration (e.g., 7 seconds)
        await Task.Delay(7000);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order = await dbContext.WorkOrders
                .Include(w => w.Crew)
                .Include(w => w.Incident)
                    .ThenInclude(i => i.Department)
                .Include(w => w.Incident)
                    .ThenInclude(i => i.ReportedBy)
                .FirstOrDefaultAsync(w => w.Id == orderId);

            if (order != null && order.Status == "InProgress")
            {
                order.Status = "Completed";
                order.CompletedAt = DateTime.UtcNow;
                
                order.Crew.Status = CrewStatus.Idle; // Free the crew
                order.Incident.Status = "Resolved"; // Mark incident resolved

                // Simulating repair costs and material budget ($200 - $1000)
                var random = new Random();
                order.BudgetSpent = (decimal)(random.Next(200, 1000));

                if (order.Incident.Department != null)
                {
                    order.Incident.Department.BudgetSpent += order.BudgetSpent;

                    if (order.Incident.Department.BudgetSpent >= order.Incident.Department.BudgetLimit * 0.85m)
                    {
                        await _hubContext.Clients.All.SendAsync("BudgetWarning", new {
                            departmentId = order.Incident.Department.Id,
                            departmentName = order.Incident.Department.Name,
                            limit = order.Incident.Department.BudgetLimit,
                            spent = order.Incident.Department.BudgetSpent,
                            ratio = (double)(order.Incident.Department.BudgetSpent / order.Incident.Department.BudgetLimit)
                        });
                    }
                }

                // Create and insert archive report
                var reportedByName = order.Incident.ReportedBy?.Username ?? "Anonymous";
                var archiveReport = new ArchiveReport
                {
                    DepartmentId = order.Incident.DepartmentId ?? 0,
                    IncidentId = order.Incident.Id,
                    IncidentTitle = order.Incident.Title,
                    IncidentDescription = order.Incident.Description,
                    ReportedBy = reportedByName,
                    ResolvedByCrew = order.Crew.Name,
                    BudgetSpent = order.BudgetSpent,
                    ReportedAt = order.Incident.CreatedAt,
                    ResolvedAt = order.CompletedAt.Value,
                    ImageUrl = order.Incident.ImageUrl
                };

                if (archiveReport.DepartmentId > 0)
                {
                    dbContext.ArchiveReports.Add(archiveReport);
                }

                await dbContext.SaveChangesAsync();

                _logger.LogInformation($"Work Order {orderId} resolved by Crew {order.Crew.Name}. Budget: ${order.BudgetSpent}. Department: {order.Incident.Department?.Name}");

                // Broadcast resolution update to frontend clients
                await _hubContext.Clients.All.SendAsync("IncidentResolved", new {
                    incidentId = incidentId,
                    crewId = crewId,
                    crewName = order.Crew.Name,
                    budgetSpent = order.BudgetSpent,
                    incidentTitle = order.Incident.Title
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing work order after delay");
        }
    }

    private double CalculateDistanceInMeters(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;

        double rLat1 = lat1 * Math.PI / 180.0;
        double rLat2 = lat2 * Math.PI / 180.0;

        double a = Math.Sin(dLat/2) * Math.Sin(dLat/2) +
                   Math.Cos(rLat1) * Math.Cos(rLat2) *
                   Math.Sin(dLon/2) * Math.Sin(dLon/2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
        double d = 6371000 * c; // Earth's radius in meters
        return d;
    }
}
