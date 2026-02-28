using DailyTrackerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<DailyLog> DailyLogs { get; set; }
        public DbSet<BreakLog> BreakLogs { get; set; }
        public DbSet<TaskLog> TaskLogs { get; set; }
        public DbSet<SupportLog> SupportLogs { get; set; }
        public DbSet<MediaEvidence> MediaEvidences { get; set; }

        // ─── Feature 11: Security ─────────────────────────────────────────────
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        // ─── Feature 4: Goals ─────────────────────────────────────────────────
        public DbSet<DailyGoal> DailyGoals { get; set; }

        // ─── Feature 6: EOD Reports ───────────────────────────────────────────
        public DbSet<EODReport> EODReports { get; set; }

        // ─── Feature 3: Task Management ───────────────────────────────────────
        public DbSet<TaskTemplate> TaskTemplates { get; set; }
        public DbSet<TaskTimer> TaskTimers { get; set; }


        // ─── Feature 9: Team Features ─────────────────────────────────────────
        public DbSet<UserPresence> UserPresences { get; set; }
        public DbSet<Kudos> Kudos { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<LeaveEmailAction> LeaveEmailActions { get; set; }

        // ─── Feature 10: Attendance Enhanced ─────────────────────────────────
        public DbSet<Holiday> Holidays { get; set; }
        public DbSet<LateArrivalReason> LateArrivalReasons { get; set; }

        // ─── Feature 1: Notifications ─────────────────────────────────────────
        public DbSet<AppNotification> Notifications { get; set; }

        // ─── Two-Factor Authentication ───────────────────────────────────────
        public DbSet<UserTwoFactor> UserTwoFactors { get; set; }
        public DbSet<TrustedDevice> TrustedDevices { get; set; }
        public DbSet<PendingLogin> PendingLogins { get; set; }
        public DbSet<WFHRequest> WFHRequests { get; set; }
        public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
        protected override void OnModelCreating(ModelBuilder mb)
        {

            // ── User
            mb.Entity<User>(e =>
            {
                // Unique Email
                e.HasIndex(u => u.Email).IsUnique();

                // Self-referencing Manager relationship
                e.HasOne(u => u.Manager)
                    .WithMany(u => u.TeamMembers)
                    .HasForeignKey(u => u.ManagerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ── DailyLog
            mb.Entity<DailyLog>(e => {
                e.HasIndex(d => new { d.UserId, d.LogDate }).IsUnique();
                e.HasOne(d => d.User)
                 .WithMany(u => u.DailyLogs)
                 .HasForeignKey(d => d.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── BreakLog
            mb.Entity<BreakLog>(e =>
                e.HasOne(b => b.DailyLog)
                 .WithMany(d => d.BreakLogs)
                 .HasForeignKey(b => b.DailyLogId)
                 .OnDelete(DeleteBehavior.Cascade));

            // ── TaskLog
            mb.Entity<TaskLog>(e =>
                e.HasOne(t => t.DailyLog)
                 .WithMany(d => d.TaskLogs)
                 .HasForeignKey(t => t.DailyLogId)
                 .OnDelete(DeleteBehavior.Cascade));

            // ── SupportLog
            mb.Entity<SupportLog>(e => {
                e.HasOne(s => s.DailyLog)
                 .WithMany(d => d.SupportLogs)
                 .HasForeignKey(s => s.DailyLogId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(s => s.SupportedDeveloper)
                 .WithMany(u => u.SupportGiven)
                 .HasForeignKey(s => s.SupportedDeveloperId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── RefreshToken
            mb.Entity<RefreshToken>(e => {
                e.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(r => r.Token).IsUnique();
            });

            // ── AuditLog
            mb.Entity<AuditLog>(e =>
                e.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId)
                 .OnDelete(DeleteBehavior.SetNull));

            // ── DailyGoal
            mb.Entity<DailyGoal>(e => {
                e.HasIndex(g => new { g.UserId, g.GoalDate }).IsUnique();
                e.HasOne(g => g.User).WithMany().HasForeignKey(g => g.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── EODReport
            mb.Entity<EODReport>(e => {
                e.HasIndex(r => new { r.UserId, r.ReportDate }).IsUnique();
                e.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(r => r.DailyLog).WithMany().HasForeignKey(r => r.DailyLogId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── TaskTemplate
            mb.Entity<TaskTemplate>(e =>
                e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
                 .OnDelete(DeleteBehavior.Cascade));

            // ── TaskTimer
            mb.Entity<TaskTimer>(e =>
                e.HasOne(t => t.TaskLog).WithMany().HasForeignKey(t => t.TaskLogId)
                 .OnDelete(DeleteBehavior.Cascade));

            // ── UserPresence
            mb.Entity<UserPresence>(e => {
                e.HasIndex(p => p.UserId).IsUnique();
                e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── Kudos
            mb.Entity<Kudos>(e => {
                e.HasOne(k => k.FromUser).WithMany().HasForeignKey(k => k.FromUserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(k => k.ToUser).WithMany().HasForeignKey(k => k.ToUserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── LeaveRequest
            mb.Entity<LeaveRequest>(e => {
                e.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(l => l.ReviewedBy).WithMany().HasForeignKey(l => l.ReviewedByUserId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ── LateArrivalReason
            mb.Entity<LateArrivalReason>(e => {
                e.HasOne(l => l.DailyLog).WithMany().HasForeignKey(l => l.DailyLogId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── AppNotification
            mb.Entity<AppNotification>(e =>
                e.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId)
                 .OnDelete(DeleteBehavior.Cascade));

            // ── Holiday - unique per date
            mb.Entity<Holiday>(e => e.HasIndex(h => h.Date).IsUnique());

            // ── UserTwoFactor (one per user)
            mb.Entity<UserTwoFactor>(e => {
                e.HasIndex(t => t.UserId).IsUnique();
                e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // ── TrustedDevice
            mb.Entity<TrustedDevice>(e => {
                e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(t => new { t.UserId, t.DeviceToken });
            });

            // ── PendingLogin (temp 2FA session)
            mb.Entity<PendingLogin>(e => {
                e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(p => p.TempToken).IsUnique();
            });

            // ── MediaEvidence (support log proof: screenshots, recordings)
            // UserId uses NoAction to avoid multiple cascade paths (User->DailyLog->SupportLog->MediaEvidences vs User->MediaEvidences)
            mb.Entity<MediaEvidence>(e => {
                e.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId)
                 .OnDelete(DeleteBehavior.NoAction);
                e.HasOne(m => m.SupportLog).WithMany(s => s.MediaEvidences)
                 .HasForeignKey(m => m.SupportLogId).OnDelete(DeleteBehavior.Cascade);
            });

            mb.Entity<WFHRequest>(e =>
            {
                e.HasIndex(r => new { r.UserId, r.RequestDate, r.Status });
                e.HasOne(r => r.User)
                 .WithMany()
                 .HasForeignKey(r => r.UserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(r => r.ReviewedBy)
                 .WithMany()
                 .HasForeignKey(r => r.ReviewedByUserId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(r => r.DailyLog)
                 .WithMany()
                 .HasForeignKey(r => r.DailyLogId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            mb.Entity<EmailOtp>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Email)
                      .IsRequired()
                      .HasMaxLength(256);

                entity.Property(e => e.Code)
                      .IsRequired()
                      .HasMaxLength(6);

                entity.Property(e => e.Purpose)
                      .IsRequired()
                      .HasMaxLength(50);

                entity.Property(e => e.ExpiresAt)
                      .IsRequired();

                entity.Property(e => e.IsUsed)
                      .HasDefaultValue(false);

                // Performance index
                entity.HasIndex(e => new { e.Email, e.Purpose });
            });
        }
    }
}
