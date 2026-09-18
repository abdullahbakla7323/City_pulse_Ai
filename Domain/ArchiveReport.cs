using System.ComponentModel.DataAnnotations;

namespace CityPulseAI.Domain.Entities;

public class ArchiveReport
{
    public int Id { get; set; }

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int IncidentId { get; set; }
    
    [Required]
    public string IncidentTitle { get; set; } = string.Empty;

    public string IncidentDescription { get; set; } = string.Empty;

    [Required]
    public string ReportedBy { get; set; } = string.Empty;

    [Required]
    public string ResolvedByCrew { get; set; } = string.Empty;

    public decimal BudgetSpent { get; set; }

    public DateTime ReportedAt { get; set; }

    public DateTime ResolvedAt { get; set; }

    public string? ImageUrl { get; set; }
}
