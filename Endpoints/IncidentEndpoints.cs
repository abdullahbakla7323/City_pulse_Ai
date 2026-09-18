using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;
using CityPulseAI.Services.Hubs;
using CityPulseAI.Services.Maps;

namespace CityPulseAI.Endpoints;

public static class IncidentEndpoints
{
    public static void MapIncidentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/incidents");

        // Get all active incidents (exclude resolved ones)
        group.MapGet("/", async (AppDbContext db) =>
            await db.Incidents
                .Where(i => i.Status != "Resolved")
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new {
                    i.Id,
                    i.Title,
                    i.Description,
                    i.Status,
                    lat = i.Location.Y,
                    lng = i.Location.X,
                    i.CreatedAt,
                    imageUrl = i.ImageUrl
                })
                .ToListAsync());

        // Get an incident by ID
        group.MapGet("/{id}", async (int id, AppDbContext db) =>
            await db.Incidents.FindAsync(id) is Incident incident
                ? Results.Ok(new {
                    incident.Id,
                    incident.Title,
                    incident.Description,
                    incident.Status,
                    lat = incident.Location.Y,
                    lng = incident.Location.X,
                    incident.CreatedAt,
                    imageUrl = incident.ImageUrl
                })
                : Results.NotFound());

        // Create a new incident directly (e.g. from citizen map click)
        group.MapPost("/", async (CreateIncidentRequest req, AppDbContext db, IHubContext<CityPulseHub> hub, SpatialService spatial) =>
        {
            var point = spatial.CreatePoint(req.Lat, req.Lng);
            var matchedType = DetectCrewType(req.Title, req.Description);
            
            Department? dept = null;
            if (matchedType.HasValue)
            {
                dept = await db.Departments.FirstOrDefaultAsync(d => d.Type == matchedType.Value);
            }

            User? user = null;
            if (!string.IsNullOrEmpty(req.ReportedBy))
            {
                user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == req.ReportedBy.ToLower());
            }

            var incident = new Incident
            {
                Title = req.Title,
                Description = req.Description,
                Location = point,
                Status = "New",
                CreatedAt = DateTime.UtcNow,
                DepartmentId = dept?.Id,
                ReportedById = user?.Id,
                ImageUrl = req.ImageUrl
            };

            db.Incidents.Add(incident);
            await db.SaveChangesAsync();

            // Auto-Pilot Dispatch
            bool autoDispatched = false;
            string? autoDispatchedCrewName = null;

            if (SystemSettings.AutoPilot)
            {
                var crew = await spatial.GetNearestCrewAsync(req.Lat, req.Lng, matchedType);
                if (crew != null && crew.Status == CrewStatus.Idle)
                {
                    var workOrder = new WorkOrder
                    {
                        IncidentId = incident.Id,
                        CrewId = crew.Id,
                        Status = "Assigned",
                        AssignedAt = DateTime.UtcNow
                    };
                    crew.Status = CrewStatus.OnWay;
                    incident.Status = "InProgress";
                    db.WorkOrders.Add(workOrder);
                    await db.SaveChangesAsync();

                    autoDispatched = true;
                    autoDispatchedCrewName = crew.Name;

                    // Broadcast assigning via SignalR to move truck on map
                    await hub.Clients.All.SendAsync("CrewAssigned", new {
                        incidentId = incident.Id,
                        incidentTitle = incident.Title,
                        crewId = crew.Id,
                        crewName = crew.Name,
                        lat = crew.Location.Y,
                        lng = crew.Location.X,
                        incidentLat = incident.Location.Y,
                        incidentLng = incident.Location.X,
                        status = crew.Status.ToString()
                    });
                }
            }

            // Broadcast the new incident location to all maps via SignalR
            await hub.Clients.All.SendAsync("IncidentReported", new
            {
                id = incident.Id,
                title = incident.Title,
                description = incident.Description,
                lat = req.Lat,
                lng = req.Lng,
                status = incident.Status,
                createdAt = incident.CreatedAt,
                departmentName = dept?.Name ?? "General Coordination Department",
                imageUrl = incident.ImageUrl,
                autoDispatched,
                autoDispatchedCrewName
            });

            return Results.Ok(new {
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Status,
                lat = req.Lat,
                lng = req.Lng,
                incident.CreatedAt,
                departmentName = dept?.Name ?? "General Coordination Department",
                imageUrl = incident.ImageUrl,
                autoDispatched,
                autoDispatchedCrewName
            });
        });

        // Media Upload Endpoint
        routes.MapPost("/api/media/upload", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Form content not found." });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file selected for upload." });
            }

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var fileUrl = $"/uploads/{uniqueFileName}";
            return Results.Ok(new { imageUrl = fileUrl });
        });

        // Assign crew to incident directly from manual UI clicks
        group.MapPost("/{id}/assign", async (int id, AssignCrewRequest req, AppDbContext db, SpatialService spatial, IHubContext<CityPulseHub> hub) =>
        {
            var incident = await db.Incidents.FindAsync(id);
            if (incident == null) return Results.NotFound(new { error = "Incident not found." });

            if (incident.Status == "Resolved") return Results.BadRequest(new { error = "This incident is already resolved." });

            Crew? crew = null;
            if (req.CrewId.HasValue)
            {
                crew = await db.Crews.FindAsync(req.CrewId.Value);
            }
            else
            {
                // Auto-detect matching crew type and find nearest
                var matchedType = DetectCrewType(incident.Title, incident.Description);
                crew = await spatial.GetNearestCrewAsync(incident.Location.Y, incident.Location.X, matchedType);
            }

            if (crew == null) return Results.BadRequest(new { error = "No available idle crew found matching this incident type." });
            if (crew.Status != CrewStatus.Idle) return Results.BadRequest(new { error = "Selected crew is not idle." });

            var workOrder = new WorkOrder
            {
                IncidentId = incident.Id,
                CrewId = crew.Id,
                Status = "Assigned",
                AssignedAt = DateTime.UtcNow
            };

            crew.Status = CrewStatus.OnWay;
            incident.Status = "InProgress";

            db.WorkOrders.Add(workOrder);
            await db.SaveChangesAsync();

            // Broadcast assigning via SignalR to move truck on map
            await hub.Clients.All.SendAsync("CrewAssigned", new {
                incidentId = incident.Id,
                incidentTitle = incident.Title,
                crewId = crew.Id,
                crewName = crew.Name,
                lat = crew.Location.Y,
                lng = crew.Location.X,
                incidentLat = incident.Location.Y,
                incidentLng = incident.Location.X,
                status = crew.Status.ToString()
            });

            return Results.Ok(new { success = true, crewName = crew.Name });
        });
    }

    public static CrewType? DetectCrewType(string title, string description)
    {
        var text = (title + " " + description).ToLowerInvariant();
        var normalized = NormalizeTurkish(text);

        // Sanitation keywords (English & Turkish)
        if (text.Contains("trash") || text.Contains("garbage") || text.Contains("waste") || text.Contains("sanitation") ||
            text.Contains("cleaning") || text.Contains("recycle") || text.Contains("litter") || text.Contains("sewer") ||
            text.Contains("drain") || text.Contains("çöp") || normalized.Contains("cop") || 
            text.Contains("temizlik") || normalized.Contains("temizlik") || 
            text.Contains("sokak temiz") || normalized.Contains("sokak temiz") || 
            text.Contains("atık") || normalized.Contains("atik") || 
            text.Contains("konteyner") || normalized.Contains("konteyner") || 
            text.Contains("süpür") || normalized.Contains("supur") || 
            text.Contains("logar") || normalized.Contains("logar") || 
            text.Contains("kanalizasyon") || normalized.Contains("kanalizasyon"))
            return CrewType.Sanitation;

        // Zoning keywords (English & Turkish)
        if (text.Contains("zoning") || text.Contains("permit") || text.Contains("urban") || text.Contains("building code") ||
            text.Contains("architecture") || text.Contains("construction permit") || text.Contains("ruhsat") || normalized.Contains("ruhsat") || 
            text.Contains("imar") || normalized.Contains("imar") || 
            text.Contains("plan") || normalized.Contains("plan") || 
            text.Contains("şehircilik") || normalized.Contains("sehircilik") || 
            text.Contains("bina") || normalized.Contains("bina") || 
            text.Contains("proje") || normalized.Contains("proje") || 
            text.Contains("yapı ruhsat") || normalized.Contains("yapi ruhsat"))
            return CrewType.Zoning;

        // Public Works & Infrastructure keywords (English & Turkish)
        if (text.Contains("asphalt") || text.Contains("pothole") || text.Contains("road") || text.Contains("street") ||
            text.Contains("pavement") || text.Contains("sidewalk") || text.Contains("infrastructure") || text.Contains("cable") ||
            text.Contains("fiber") || text.Contains("internet") || text.Contains("electric") || text.Contains("power") ||
            text.Contains("utility") || text.Contains("water main") || text.Contains("pipeline") || text.Contains("bridge") ||
            text.Contains("çukur") || normalized.Contains("cukur") || 
            text.Contains("yol") || normalized.Contains("yol") || 
            text.Contains("kaldırım") || normalized.Contains("kaldirim") || 
            text.Contains("parke") || normalized.Contains("parke") || 
            text.Contains("inşaat") || normalized.Contains("insaat") || 
            text.Contains("geçit") || normalized.Contains("gecit") || 
            text.Contains("altyapı") || normalized.Contains("altyapi") || 
            text.Contains("kablo") || normalized.Contains("kablo") || 
            text.Contains("telefon") || normalized.Contains("telefon") || 
            text.Contains("şebeke") || normalized.Contains("sebeke") || 
            text.Contains("iletişim") || normalized.Contains("iletisim") || 
            text.Contains("direk") || normalized.Contains("direk") || 
            text.Contains("elektrik") || normalized.Contains("elektrik"))
            return CrewType.PublicWorks;

        // Parks & Green Spaces keywords (English & Turkish)
        if (text.Contains("tree") || text.Contains("park") || text.Contains("green") || text.Contains("garden") ||
            text.Contains("landscape") || text.Contains("flower") || text.Contains("pruning") || text.Contains("lawn") ||
            text.Contains("ağaç") || normalized.Contains("agac") || 
            text.Contains("yeşil") || normalized.Contains("yesil") || 
            text.Contains("peyzaj") || normalized.Contains("peyzaj") || 
            text.Contains("bahçe") || normalized.Contains("bahce") || 
            text.Contains("çiçek") || normalized.Contains("cicek") || 
            text.Contains("budama") || normalized.Contains("budama"))
            return CrewType.Parks;

        return null;
    }

    private static string NormalizeTurkish(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("ç", "c")
                   .Replace("ğ", "g")
                   .Replace("ı", "i")
                   .Replace("ö", "o")
                   .Replace("ş", "s")
                   .Replace("ü", "u");
    }
}

public class CreateIncidentRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string? ReportedBy { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public class AssignCrewRequest
{
    public int? CrewId { get; set; }
}
