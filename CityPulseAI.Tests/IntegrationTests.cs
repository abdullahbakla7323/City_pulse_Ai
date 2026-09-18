using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CityPulseAI.Infrastructure.Data;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;
using CityPulseAI.Services.Maps;
using CityPulseAI.Services.Reports;
using CityPulseAI.Services.Gemini;
using CityPulseAI.Endpoints;

namespace CityPulseAI.Tests
{
    public class IntegrationTests
    {
        private string? FindEnvFile()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envPath))
                {
                    return envPath;
                }
                dir = dir.Parent;
            }
            return null;
        }

        private AppDbContext GetDbContext()
        {
            var envFile = FindEnvFile();
            if (envFile != null)
            {
                DotNetEnv.Env.Load(envFile);
            }

            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
                                   ?? "Host=localhost;Database=citypulse;Username=postgres;Password=;SSL Mode=Prefer;Trust Server Certificate=true";

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(connectionString, o => o.UseNetTopologySuite());
            return new AppDbContext(optionsBuilder.Options);
        }

        [Fact]
        public void TestDatabaseConnectionAndSeedData()
        {
            using var db = GetDbContext();
            
            // Check connection
            Assert.True(db.Database.CanConnect(), "Cannot connect to database. Please make sure PostgreSQL/PostGIS is running.");

            // Check if departments are seeded
            var depts = db.Departments.ToList();
            Assert.True(depts.Count >= 4, $"Departments missing in database. Found: {depts.Count}");

            // Verify the 4 departments exist
            Assert.Contains(depts, d => d.Type == CrewType.Sanitation);
            Assert.Contains(depts, d => d.Type == CrewType.Zoning);
            Assert.Contains(depts, d => d.Type == CrewType.PublicWorks);
            Assert.Contains(depts, d => d.Type == CrewType.Parks);

            // Check if crews are seeded
            var crews = db.Crews.ToList();
            Assert.True(crews.Count >= 4, $"Crews missing in database. Found: {crews.Count}");

            // Check if default users are seeded
            var users = db.Users.ToList();
            Assert.Contains(users, u => u.Username.ToLower() == "admin");
            Assert.Contains(users, u => u.Username.ToLower() == "citizen");
            Assert.Contains(users, u => u.Username.ToLower() == "dispatcher");

            // Check if region boundaries exist
            var regions = db.Regions.ToList();
            Assert.NotEmpty(regions);
            Assert.Contains(regions, r => r.Name.Contains("Zone") || r.Name.Contains("District") || r.Name.Contains("Kadıköy"));
        }

        [Fact]
        public async System.Threading.Tasks.Task TestSpatialServiceNearestCrewCalculation()
        {
            using var db = GetDbContext();
            var spatial = new SpatialService(db);

            // Coordinates for mock incident (e.g. 40.99, 29.03)
            double lat = 40.99;
            double lng = 29.03;

            // Search for nearest PublicWorks crew
            var nearestCrew = await spatial.GetNearestCrewAsync(lat, lng, CrewType.PublicWorks);

            Assert.NotNull(nearestCrew);
            Assert.Equal(CrewType.PublicWorks, nearestCrew.Type);
            Assert.Equal(CrewStatus.Idle, nearestCrew.Status); // Initial state should be Idle
        }

        [Fact]
        public void TestReportPdfGeneration()
        {
            // 1. Operation Report PDF Test
            var mockReport = new ArchiveReport
            {
                Id = 1,
                IncidentId = 100,
                IncidentTitle = "Test Pothole Incident",
                IncidentDescription = "Test Incident Description for Pothole",
                ReportedBy = "citizen",
                ResolvedByCrew = "Public Works Crew W1",
                BudgetSpent = 1500.50m,
                ReportedAt = DateTime.UtcNow.AddHours(-1),
                ResolvedAt = DateTime.UtcNow,
                DepartmentId = 1
            };

            var operationPdfBytes = ReportPdfService.GenerateReportPdf(mockReport);
            Assert.NotNull(operationPdfBytes);
            Assert.True(operationPdfBytes.Length > 0, "Operation Report PDF generation failed.");

            // 2. Crews Status Report PDF Test
            var geometryFactory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
            var mockCrews = new List<Crew>
            {
                new Crew
                {
                    Id = 1,
                    Name = "Test Sanitation Crew 1",
                    Type = CrewType.Sanitation,
                    Status = CrewStatus.Idle,
                    Location = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(29.03, 40.99))
                },
                new Crew
                {
                    Id = 2,
                    Name = "Test Public Works Crew 1",
                    Type = CrewType.PublicWorks,
                    Status = CrewStatus.Busy,
                    Location = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(29.04, 40.98))
                }
            };

            var crewsPdfBytes = ReportPdfService.GenerateCrewsStatusPdf(mockCrews);
            Assert.NotNull(crewsPdfBytes);
            Assert.True(crewsPdfBytes.Length > 0, "Crews Status Report PDF generation failed.");

            // 3. System General Summary Report Test
            var mockActiveIncidents = new List<Incident>
            {
                new Incident
                {
                    Id = 1,
                    Title = "Active Road Repair",
                    Description = "Pothole formed on main street",
                    Status = "New",
                    CreatedAt = DateTime.UtcNow,
                    Location = geometryFactory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(29.02, 41.00))
                }
            };

            var systemPdfBytes = ReportPdfService.GenerateSystemSummaryPdf(
                totalIncidents: 10,
                activeIncidents: 3,
                resolvedIncidents: 7,
                idleCrews: 2,
                busyCrews: 3,
                totalBudgetSpent: 25000.40m,
                activeIncidentsList: mockActiveIncidents
            );

            Assert.NotNull(systemPdfBytes);
            Assert.True(systemPdfBytes.Length > 0, "System Summary Report PDF generation failed.");
        }

        [Fact]
        public void TestCrewTypeDetection()
        {
            // Test crew type detection for English & Turkish keywords
            var typePW1 = IncidentEndpoints.DetectCrewType("Pothole on the avenue", "Road asphalt is broken");
            Assert.Equal(CrewType.PublicWorks, typePW1);

            var typeSan1 = IncidentEndpoints.DetectCrewType("Trash overflow", "Garbage container is full");
            Assert.Equal(CrewType.Sanitation, typeSan1);

            var typePark1 = IncidentEndpoints.DetectCrewType("Fallen tree branch", "Park greenery needs pruning");
            Assert.Equal(CrewType.Parks, typePark1);

            var typeZoning1 = IncidentEndpoints.DetectCrewType("Building permit check", "Zoning inspection required");
            Assert.Equal(CrewType.Zoning, typeZoning1);

            var typePW2 = IncidentEndpoints.DetectCrewType("cukur var yolda", "asfalt bozulmus");
            Assert.Equal(CrewType.PublicWorks, typePW2);

            var typeSan2 = IncidentEndpoints.DetectCrewType("cop birikti", "konteyner tasti");
            Assert.Equal(CrewType.Sanitation, typeSan2);
        }
    }
}
