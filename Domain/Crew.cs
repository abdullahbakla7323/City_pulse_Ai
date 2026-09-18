using System.ComponentModel.DataAnnotations;
using NetTopologySuite.Geometries;

namespace CityPulseAI.Domain.Entities;

public class Crew
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public CrewType Type { get; set; }

    public CrewStatus Status { get; set; }

    // NetTopologySuite Point mapping to PostGIS GEOMETRY(Point, 4326)
    public Point Location { get; set; } = null!;

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    [Required]
    public string Members { get; set; } = string.Empty;
}
