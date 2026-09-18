using System.ComponentModel.DataAnnotations;
using NetTopologySuite.Geometries;

namespace CityPulseAI.Domain.Entities;

public class Region
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    // NetTopologySuite Polygon mapping to PostGIS GEOMETRY(Polygon, 4326)
    public Polygon Area { get; set; } = null!;
}
