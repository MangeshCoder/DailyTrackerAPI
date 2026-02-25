using System.ComponentModel.DataAnnotations;

namespace DailyTrackerAPI.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public string Role { get; set; } = "Developer"; // Developer, TeamLead, Manager

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
        public int? ManagerId { get; set; }
        public User? Manager { get; set; }
        public ICollection<User> TeamMembers { get; set; } = new List<User>();

        // Navigation
        public ICollection<DailyLog> DailyLogs { get; set; } = new List<DailyLog>();
        public ICollection<SupportLog> SupportGiven { get; set; } = new List<SupportLog>();
    }
}
