using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Domain;
using CityPulseAI.Services.Hubs;

namespace CityPulseAI.Endpoints;

public static class DepartmentEndpoints
{
    public static void MapDepartmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/departments");

        // Get all departments with their current live statistics
        group.MapGet("/", async (AppDbContext db) =>
        {
            var depts = await db.Departments.ToListAsync();
            
            var results = new List<object>();
            foreach (var d in depts)
            {
                // Count active incidents associated with this department
                var activeIncidentsCount = await db.Incidents
                    .CountAsync(i => i.DepartmentId == d.Id && i.Status != "Resolved");

                // Count total resolved incidents associated with this department
                var resolvedIncidentsCount = await db.Incidents
                    .CountAsync(i => i.DepartmentId == d.Id && i.Status == "Resolved");

                // Count active crews (busy or on way) and total crews under this department
                var crews = await db.Crews
                    .Where(c => c.DepartmentId == d.Id)
                    .ToListAsync();

                var activeCrewsCount = crews.Count(c => c.Status == CrewStatus.Busy || c.Status == CrewStatus.OnWay);
                var totalCrewsCount = crews.Count;

                // Retrieve lists for frontend display
                var activeIncidents = await db.Incidents
                    .Where(i => i.DepartmentId == d.Id && i.Status != "Resolved")
                    .OrderByDescending(i => i.CreatedAt)
                    .Take(5)
                    .Select(i => new { i.Id, i.Title, i.Status, i.ImageUrl })
                    .ToListAsync();

                var departmentCrews = crews
                    .Select(c => new { c.Id, c.Name, Status = c.Status.ToString(), members = c.Members })
                    .ToList();

                results.Add(new
                {
                    d.Id,
                    d.Name,
                    Type = d.Type.ToString(),
                    d.ManagerName,
                    d.PhoneNumber,
                    d.BudgetLimit,
                    d.BudgetSpent,
                    BudgetRemaining = d.BudgetLimit - d.BudgetSpent,
                    ActiveIncidentsCount = activeIncidentsCount,
                    ResolvedIncidentsCount = resolvedIncidentsCount,
                    ActiveCrewsCount = activeCrewsCount,
                    TotalCrewsCount = totalCrewsCount,
                    ActiveIncidents = activeIncidents,
                    Crews = departmentCrews
                });
            }

            return Results.Ok(results);
        });

        // Get archive reports for a specific department
        group.MapGet("/{id}/archive", async (int id, AppDbContext db) =>
        {
            var reports = await db.ArchiveReports
                .Where(r => r.DepartmentId == id)
                .OrderByDescending(r => r.ResolvedAt)
                .Select(r => new {
                    r.Id,
                    r.IncidentId,
                    r.IncidentTitle,
                    r.IncidentDescription,
                    r.ReportedBy,
                    r.ResolvedByCrew,
                    r.BudgetSpent,
                    r.ReportedAt,
                    r.ResolvedAt,
                    r.ImageUrl
                })
                .ToListAsync();

            return Results.Ok(reports);
        });

        // Download official PDF report for a resolved incident
        routes.MapGet("/api/reports/{id}/pdf", async (int id, bool? download, AppDbContext db) =>
        {
            var report = await db.ArchiveReports
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null)
            {
                return Results.NotFound(new { error = "Report not found." });
            }

            try
            {
                var pdfBytes = Services.Reports.ReportPdfService.GenerateReportPdf(report);
                if (download == true)
                {
                    return Results.File(pdfBytes, "application/pdf", $"CityPulse_Operation_Report_{report.Id}.pdf");
                }
                else
                {
                    return Results.File(pdfBytes, "application/pdf");
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error generating PDF: {ex.Message}");
            }
        });

        // Transfer budget from one department to another
        group.MapPost("/transfer-budget", async (TransferBudgetRequest req, AppDbContext db, IHubContext<CityPulseHub> hub) =>
        {
            if (req.FromDepartmentId == req.ToDepartmentId)
                return Results.BadRequest(new { error = "Source and target departments cannot be the same." });

            var fromDept = await db.Departments.FindAsync(req.FromDepartmentId);
            var toDept = await db.Departments.FindAsync(req.ToDepartmentId);

            if (fromDept == null || toDept == null)
                return Results.BadRequest(new { error = "Department not found." });

            var availableLimit = fromDept.BudgetLimit - fromDept.BudgetSpent;
            if (availableLimit < req.Amount)
                return Results.BadRequest(new { error = $"Insufficient budget available. Maximum transferable amount: ${availableLimit:N2}" });

            // Transfer limit
            fromDept.BudgetLimit -= req.Amount;
            toDept.BudgetLimit += req.Amount;

            await db.SaveChangesAsync();

            // Broadcast update via SignalR so charts/cards update instantly
            await hub.Clients.All.SendAsync("BudgetTransferred", new {
                fromDepartmentId = fromDept.Id,
                fromDepartmentName = fromDept.Name,
                fromLimit = fromDept.BudgetLimit,
                fromSpent = fromDept.BudgetSpent,
                toDepartmentId = toDept.Id,
                toDepartmentName = toDept.Name,
                toLimit = toDept.BudgetLimit,
                toSpent = toDept.BudgetSpent,
                amount = req.Amount
            });

            return Results.Ok(new { success = true });
        });

        // Add member to crew
        group.MapPost("/crews/{crewId}/members/add", async (int crewId, AddMemberRequest req, AppDbContext db, IHubContext<CityPulseHub> hub) =>
        {
            var crew = await db.Crews.FindAsync(crewId);
            if (crew == null)
                return Results.NotFound(new { error = "Crew not found." });

            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Staff name cannot be empty." });

            var memberList = string.IsNullOrEmpty(crew.Members) 
                ? new List<string>() 
                : crew.Members.Split(',').Select(m => m.Trim()).ToList();

            if (memberList.Any(m => m.Equals(req.Name, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { error = "This staff member is already on the crew." });

            memberList.Add(req.Name.Trim());
            crew.Members = string.Join(", ", memberList);
            await db.SaveChangesAsync();

            // Broadcast updates
            await hub.Clients.All.SendAsync("CrewMembersUpdated", new { crewId = crew.Id, members = crew.Members });

            return Results.Ok(new { success = true, members = crew.Members });
        });

        // Remove member from crew
        group.MapPost("/crews/{crewId}/members/remove", async (int crewId, RemoveMemberRequest req, AppDbContext db, IHubContext<CityPulseHub> hub) =>
        {
            var crew = await db.Crews.FindAsync(crewId);
            if (crew == null)
                return Results.NotFound(new { error = "Crew not found." });

            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Staff name cannot be empty." });

            var memberList = string.IsNullOrEmpty(crew.Members) 
                ? new List<string>() 
                : crew.Members.Split(',').Select(m => m.Trim()).ToList();

            var existingMember = memberList.FirstOrDefault(m => m.Equals(req.Name, StringComparison.OrdinalIgnoreCase));
            if (existingMember == null)
                return Results.BadRequest(new { error = "Staff member not found in this crew." });

            memberList.Remove(existingMember);
            crew.Members = string.Join(", ", memberList);
            await db.SaveChangesAsync();

            // Broadcast updates
            await hub.Clients.All.SendAsync("CrewMembersUpdated", new { crewId = crew.Id, members = crew.Members });

            return Results.Ok(new { success = true, members = crew.Members });
        });
    }
}

public record TransferBudgetRequest(int FromDepartmentId, int ToDepartmentId, decimal Amount);
public record AddMemberRequest(string Name);
public record RemoveMemberRequest(string Name);
