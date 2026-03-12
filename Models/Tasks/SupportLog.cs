using DailyTrackerAPI.Models.Auth;
using System.ComponentModel.DataAnnotations;

namespace DailyTrackerAPI.Models.Tasks
{
    public class SupportLog
    {
        public int Id { get; set; }

        public int DailyLogId { get; set; }
        public DailyLog DailyLog { get; set; } = null!;

        public int SupportedDeveloperId { get; set; }
        public User SupportedDeveloper { get; set; } = null!;

        [Required, MaxLength(500)]
        public string IssueDescription { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Resolution { get; set; }

        public int TimeSpentMinutes { get; set; } = 0;

        public string SupportType { get; set; } = "Technical"; // Technical, CodeReview, Debugging, Deployment

        public DateTime SupportedAt { get; set; } = DateTime.UtcNow;

        public ICollection<MediaEvidence> MediaEvidences { get; set; } = new List<MediaEvidence>();
    }
}
