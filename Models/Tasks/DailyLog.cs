using DailyTrackerAPI.Models.Attendance;
using DailyTrackerAPI.Models.Auth;
using System.ComponentModel.DataAnnotations;

namespace DailyTrackerAPI.Models.Tasks
{
    public class DailyLog
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public DateTime LogDate { get; set; } = DateTime.UtcNow.Date;

        public DateTime? CheckInTime { get; set; }

        public DateTime? CheckOutTime { get; set; }

        public int TotalWorkMinutes { get; set; } = 0;

        public int TotalBreakMinutes { get; set; } = 0;

        public string DayStatus { get; set; } = "Present"; // Present, WFH, HalfDay, Absent

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<BreakLog> BreakLogs { get; set; } = new List<BreakLog>();
        public ICollection<TaskLog> TaskLogs { get; set; } = new List<TaskLog>();
        public ICollection<SupportLog> SupportLogs { get; set; } = new List<SupportLog>();
    }
}
