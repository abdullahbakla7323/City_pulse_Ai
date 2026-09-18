using System.ComponentModel.DataAnnotations;

namespace CityPulseAI.Domain.Entities;

public class WorkOrder
{
    public int Id { get; set; }

    public int IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;

    public int CrewId { get; set; }
    public Crew Crew { get; set; } = null!;

    public decimal BudgetSpent { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public string Status { get; set; } = "Assigned"; // Assigned, Completed
}
