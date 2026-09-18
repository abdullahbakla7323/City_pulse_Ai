using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite;
using CityPulseAI.Domain;
using CityPulseAI.Domain.Entities;
using CityPulseAI.Infrastructure.Data;

namespace CityPulseAI.Services.Maps;

public class SpatialService
{
    private readonly AppDbContext _dbContext;
    private readonly GeometryFactory _geometryFactory;

    public SpatialService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        // WGS 84 coordinate system geometry factory
        _geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
    }

    /// <summary>
    /// Finds the nearest idle crew of a specific type (if requested) to the coordinates using PostGIS ST_Distance.
    /// </summary>
    public async Task<Crew?> GetNearestCrewAsync(double latitude, double longitude, CrewType? requiredType = null)
    {
        // NetTopologySuite: Coordinate parameters are (x, y) which map to (longitude, latitude)
        var searchPoint = _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

        var query = _dbContext.Crews
            .Where(c => c.Status == CrewStatus.Idle);

        if (requiredType.HasValue)
        {
            query = query.Where(c => c.Type == requiredType.Value);
        }

        // Ordered by PostGIS spatial distance calculation (translated to ST_Distance in PostgreSQL)
        return await query
            .OrderBy(c => c.Location.Distance(searchPoint))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Returns all incidents located inside the polygon boundaries of a given Region.
    /// Uses PostGIS ST_Contains / ST_Within behind the scenes.
    /// </summary>
    public async Task<List<Incident>> GetIncidentsInRegionAsync(int regionId)
    {
        var region = await _dbContext.Regions.FindAsync(regionId);
        if (region == null) return new List<Incident>();

        return await _dbContext.Incidents
            .Where(i => region.Area.Contains(i.Location))
            .ToListAsync();
    }

    /// <summary>
    /// Helper to convert latitude/longitude coordinates to a NetTopologySuite Point.
    /// </summary>
    public Point CreatePoint(double latitude, double longitude)
    {
        return _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));
    }
}
