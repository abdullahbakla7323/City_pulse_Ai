using Microsoft.EntityFrameworkCore;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;

namespace CityPulseAI.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Crew> Crews => Set<Crew>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ArchiveReport> ArchiveReports => Set<ArchiveReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tell EF Core to use the PostGIS extension in PostgreSQL
        modelBuilder.HasPostgresExtension("postgis");

        // Explicitly define spatial columns as WGS 84 coordinates (SRID 4326)
        modelBuilder.Entity<Crew>()
            .Property(c => c.Location)
            .HasColumnType("geometry(Point, 4326)");

        modelBuilder.Entity<Incident>()
            .Property(i => i.Location)
            .HasColumnType("geometry(Point, 4326)");

        modelBuilder.Entity<Region>()
            .Property(r => r.Area)
            .HasColumnType("geometry(Polygon, 4326)");

        // Set up relationships and cascade paths
        modelBuilder.Entity<WorkOrder>()
            .HasOne(w => w.Incident)
            .WithMany()
            .HasForeignKey(w => w.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkOrder>()
            .HasOne(w => w.Crew)
            .WithMany()
            .HasForeignKey(w => w.CrewId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Incident>()
            .HasOne(i => i.ReportedBy)
            .WithMany()
            .HasForeignKey(i => i.ReportedById)
            .OnDelete(DeleteBehavior.SetNull);

        // Department relationships
        modelBuilder.Entity<Crew>()
            .HasOne(c => c.Department)
            .WithMany()
            .HasForeignKey(c => c.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Incident>()
            .HasOne(i => i.Department)
            .WithMany()
            .HasForeignKey(i => i.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ArchiveReport>()
            .HasOne(r => r.Department)
            .WithMany()
            .HasForeignKey(r => r.DepartmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
