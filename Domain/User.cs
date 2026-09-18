using System.ComponentModel.DataAnnotations;

namespace CityPulseAI.Domain.Entities;

public class User
{
    public int Id { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty; // Plaintext for school project simplicity

    public UserRole Role { get; set; }
}
