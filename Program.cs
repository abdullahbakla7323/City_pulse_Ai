using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Diagnostics;
using Photino.NET;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Endpoints;
using CityPulseAI.Services.Gemini;
using CityPulseAI.Services.Hubs;
using CityPulseAI.Services.Maps;

namespace CityPulseAI;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Load Environment Variables from .env file (traverse parent directories to find root .env)
        try
        {
            DotNetEnv.Env.TraversePath().Load();
        }
        catch { }

        // Set working directory to the executable's directory so relative paths work when launched from Finder/Desktop
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        var builder = WebApplication.CreateBuilder(args);

        // Configure Kestrel to run on Loopback (127.0.0.1) with a dynamic port (0) to prevent port conflicts
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Loopback, 0);
        });

        // 2. Configure Database Context (PostgreSQL + PostGIS spatial mapping)
        var connectionString = builder.Configuration["DATABASE_URL"] 
                               ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                               ?? "Host=localhost;Database=citypulse;Username=citypulse_user;Password=citypulse_secret;SSL Mode=Prefer;Trust Server Certificate=true";

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseNetTopologySuite()));

        // 3. Register Core Services
        builder.Services.AddScoped<SpatialService>();
        builder.Services.AddScoped<GeminiAgentService>();
        
        // SignalR for real-time WebSockets
        builder.Services.AddSignalR();

        // 4. Register Crew Movement Background Simulator
        builder.Services.AddHostedService<CrewSimulationService>();

        // Logging
        builder.Services.AddLogging(config =>
        {
            config.AddConsole();
            config.AddDebug();
        });

        var app = builder.Build();

        // 5. Configure Web Middlewares
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Map Websocket Hub
        app.MapHub<CityPulseHub>("/citypulsehub");

        // 6. Map Endpoints
        app.MapIncidentEndpoints();
        app.MapCrewEndpoints();
        app.MapGeminiEndpoints();
        app.MapMetricsEndpoints();
        app.MapAuthEndpoints();
        app.MapDepartmentEndpoints();
        app.MapSettingsEndpoints();

        // Initialize Database & Seed data in a background context
        InitializeDatabase(app);

        // 7. Start Kestrel Web Server in the background
        app.StartAsync().GetAwaiter().GetResult();

        // Retrieve Kestrel local address and dynamic port
        var localAddress = app.Urls.FirstOrDefault() ?? "http://127.0.0.1:5000";
        Console.WriteLine($"[CityPulse Server] Backend Web Server listening on: {localAddress}");

        // 8. Open Photino Native Webview Desktop Window
        var window = new PhotinoWindow();
        window.SetTitle("CityPulse AI - Smart City Management Platform");
        window.SetUseOsDefaultSize(false);
        window.SetWidth(1380);
        window.SetHeight(880);
        window.Center();

        // Handle message from frontend to open URLs in external browser or handle local PDF operations
        window.RegisterWebMessageReceivedHandler((sender, message) =>
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString();
                    if (type == "open_url")
                    {
                        if (root.TryGetProperty("url", out var urlProp))
                        {
                            var url = urlProp.GetString();
                            if (!string.IsNullOrEmpty(url))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = url,
                                    UseShellExecute = true
                                });
                            }
                        }
                    }
                    else if (type == "open_pdf_local")
                    {
                        if (root.TryGetProperty("id", out var idProp))
                        {
                            int reportId = 0;
                            if (idProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                reportId = idProp.GetInt32();
                            }
                            else if (idProp.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(idProp.GetString(), out var parsedId))
                            {
                                reportId = parsedId;
                            }

                            if (reportId > 0)
                            {
                                using var scope = app.Services.CreateScope();
                                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var report = db.ArchiveReports
                                    .Include(r => r.Department)
                                    .FirstOrDefault(r => r.Id == reportId);

                                if (report != null)
                                {
                                    var pdfBytes = Services.Reports.ReportPdfService.GenerateReportPdf(report);
                                    var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp_reports");
                                    if (!Directory.Exists(tempDir))
                                    {
                                        Directory.CreateDirectory(tempDir);
                                    }

                                    var filePath = Path.Combine(tempDir, $"CityPulse_Operation_Report_{report.Id}.pdf");
                                    File.WriteAllBytes(filePath, pdfBytes);

                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = filePath,
                                        UseShellExecute = true
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling web message: {ex.Message}");
            }
        });

        window.Load(localAddress); // Point Photino window to localhost server

        // Blocking call: blocks thread until the desktop window is closed
        window.WaitForClose();

        // 9. Shutdown Web Server gracefully when UI closes
        Console.WriteLine("[CityPulse Server] Desktop window closed. Shutting down backend services...");
        app.StopAsync().GetAwaiter().GetResult();
    }

    private static void InitializeDatabase(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spatial = scope.ServiceProvider.GetRequiredService<SpatialService>();

        try
        {
            // Apply migrations automatically
            db.Database.Migrate();

            // Seed default users if empty
            if (!db.Users.Any())
            {
                Console.WriteLine("[Database Seed] Seeding default users...");
                db.Users.AddRange(
                    new User { Username = "admin", Password = "adminpassword", Role = UserRole.Admin },
                    new User { Username = "citizen", Password = "userpassword", Role = UserRole.Citizen },
                    new User { Username = "dispatcher", Password = "dispatcherpassword", Role = UserRole.Dispatcher }
                );
                db.SaveChanges();
            }

            // If departments contain Turkish names, reset tables to reseed clean English structure
            var hasTurkishDepts = db.Departments.Any(d => d.Name.Contains("Müdürlüğü") || d.Name.Contains("Daire"));
            if (hasTurkishDepts)
            {
                Console.WriteLine("[Database Seed] Turkish departments detected. Reseeding with English naming...");
                db.WorkOrders.ExecuteDelete();
                db.ArchiveReports.ExecuteDelete();
                db.Incidents.ExecuteDelete();
                db.Crews.ExecuteDelete();
                db.Departments.ExecuteDelete();
            }

            // Seed default departments if empty
            if (!db.Departments.Any())
            {
                Console.WriteLine("[Database Seed] Seeding default departments...");
                db.Departments.AddRange(
                    new Department { Name = "Sanitation Department", Type = CrewType.Sanitation, ManagerName = "John Miller", PhoneNumber = "+1 (555) 011-0110", BudgetLimit = 500000, BudgetSpent = 0 },
                    new Department { Name = "Urban Planning & Zoning Department", Type = CrewType.Zoning, ManagerName = "Sarah Jenkins", PhoneNumber = "+1 (555) 013-0130", BudgetLimit = 800000, BudgetSpent = 0 },
                    new Department { Name = "Public Works & Infrastructure Department", Type = CrewType.PublicWorks, ManagerName = "Michael Brown", PhoneNumber = "+1 (555) 012-0120", BudgetLimit = 1200000, BudgetSpent = 0 },
                    new Department { Name = "Parks & Recreation Department", Type = CrewType.Parks, ManagerName = "Emily Davis", PhoneNumber = "+1 (555) 014-0140", BudgetLimit = 600000, BudgetSpent = 0 }
                );
                db.SaveChanges();
            }

            // Check if seeding is needed
            if (!db.Crews.Any())
            {
                Console.WriteLine("[Database Seed] Seeding default crews...");
                
                var depts = db.Departments.ToList();
                var temDept = depts.FirstOrDefault(d => d.Type == CrewType.Sanitation);
                var imarDept = depts.FirstOrDefault(d => d.Type == CrewType.Zoning);
                var fenDept = depts.FirstOrDefault(d => d.Type == CrewType.PublicWorks);
                var prkDept = depts.FirstOrDefault(d => d.Type == CrewType.Parks);

                // Initialize default crews in different locations
                db.Crews.AddRange(
                    new Crew { Name = "Central Sanitation Crew S1", Type = CrewType.Sanitation, Status = CrewStatus.Idle, Location = spatial.CreatePoint(40.9918, 29.0270), DepartmentId = temDept?.Id, Members = "Kevin Miller, Brian Clark" },
                    new Crew { Name = "Zoning Inspection Crew Z1", Type = CrewType.Zoning, Status = CrewStatus.Idle, Location = spatial.CreatePoint(40.9790, 29.0601), DepartmentId = imarDept?.Id, Members = "David Wilson, Rachel Green" },
                    new Crew { Name = "Public Works Crew W1", Type = CrewType.PublicWorks, Status = CrewStatus.Idle, Location = spatial.CreatePoint(40.9575, 29.0945), DepartmentId = fenDept?.Id, Members = "James Harris, Robert King" },
                    new Crew { Name = "Downtown Parks Crew P1", Type = CrewType.Parks, Status = CrewStatus.Idle, Location = spatial.CreatePoint(40.9850, 29.0180), DepartmentId = prkDept?.Id, Members = "Alice Johnson, Eric Scott" },
                    new Crew { Name = "Eastside Sanitation Crew S2", Type = CrewType.Sanitation, Status = CrewStatus.Idle, Location = spatial.CreatePoint(40.9760, 29.0950), DepartmentId = temDept?.Id, Members = "Sam Carter, Oliver White" },
                    new Crew { Name = "Northside Public Works Crew W2", Type = CrewType.PublicWorks, Status = CrewStatus.Idle, Location = spatial.CreatePoint(40.9650, 29.0750), DepartmentId = fenDept?.Id, Members = "Thomas Wright, Mark Evans" }
                );

                db.SaveChanges();
            }
            else
            {
                // Ensure existing crews have members seeded if they are empty
                var existingCrews = db.Crews.ToList();
                if (existingCrews.Any())
                {
                    foreach (var crew in existingCrews)
                    {
                        if (string.IsNullOrEmpty(crew.Members))
                        {
                            crew.Members = "Kevin Miller, Brian Clark";
                        }
                    }
                    db.SaveChanges();
                }

                // Link existing crews with departments if their DepartmentId is null
                var crewsWithNullDept = db.Crews.Where(c => c.DepartmentId == null).ToList();
                if (crewsWithNullDept.Any())
                {
                    Console.WriteLine("[Database Seed] Associating existing crews with departments...");
                    var depts = db.Departments.ToList();
                    foreach (var crew in crewsWithNullDept)
                    {
                        var matchingDept = depts.FirstOrDefault(d => d.Type == crew.Type);
                        if (matchingDept != null)
                        {
                            crew.DepartmentId = matchingDept.Id;
                        }
                    }
                    db.SaveChanges();
                }
            }

            if (!db.Regions.Any())
            {
                Console.WriteLine("[Database Seed] Seeding region boundaries...");

                var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
                var shellCoordinates = new[]
                {
                    new NetTopologySuite.Geometries.Coordinate(29.010, 41.010),
                    new NetTopologySuite.Geometries.Coordinate(29.040, 41.010),
                    new NetTopologySuite.Geometries.Coordinate(29.040, 40.970),
                    new NetTopologySuite.Geometries.Coordinate(29.010, 40.970),
                    new NetTopologySuite.Geometries.Coordinate(29.010, 41.010) // Close the polygon loop
                };

                var polygon = geometryFactory.CreatePolygon(shellCoordinates);

                db.Regions.Add(new Region
                {
                    Name = "Central District Zone",
                    Area = polygon
                });

                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Database Seed] Warning: Database seeding failed: {ex.Message}");
        }
    }
}
