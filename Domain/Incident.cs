using System.ComponentModel.DataAnnotations;
using NetTopologySuite.Geometries;

namespace CityPulseAI.Domain.Entities;

public class Incident
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Status { get; set; } = "New"; // New, InProgress, Resolved

    // NetTopologySuite Point mapping to PostGIS GEOMETRY(Point, 4326)
    public Point Location { get; set; } = null!;

    public int? ReportedById { get; set; }
    public User? ReportedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string? ImageUrl { get; set; }
}
