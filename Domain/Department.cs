using System.ComponentModel.DataAnnotations;

namespace CityPulseAI.Domain.Entities;

public class Department
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public CrewType Type { get; set; }

    [Required]
    public string ManagerName { get; set; } = string.Empty;

    [Required]
    public string PhoneNumber { get; set; } = string.Empty;

    public decimal BudgetLimit { get; set; }

    public decimal BudgetSpent { get; set; }
}
