using Microsoft.EntityFrameworkCore;
using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models;

namespace DailyTrackerAPI.Services
{
    public interface IDailyLogService
    {
        Task<DailyLogResponseDto?> CheckInAsync(int userId, CheckInDto dto);
        Task<DailyLogResponseDto?> CheckOutAsync(int userId, CheckOutDto dto);
        Task<DailyLogResponseDto?> GetTodayLogAsync(int userId);
        Task<DailyLogResponseDto?> GetLogByDateAsync(int userId, DateTime date);
        Task<List<DailyLogResponseDto>> GetHistoryAsync(int userId, int days = 30);
    }

    public class DailyLogService : IDailyLogService
    {
        private readonly AppDbContext _db;

        public DailyLogService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<DailyLogResponseDto?> CheckInAsync(int userId, CheckInDto dto)
        {
            var today = DateTime.UtcNow.Date;
            var existing = await _db.DailyLogs.FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today);
            if (existing != null) return await MapToDtoAsync(existing.Id);

            var log = new DailyLog
            {
                UserId = userId,
                LogDate = today,
                CheckInTime = DateTime.UtcNow,
                DayStatus = dto.DayStatus,
                Notes = dto.Notes
            };

            _db.DailyLogs.Add(log);
            await _db.SaveChangesAsync();
            var approvedRequest = await _db.WFHRequests
            .FirstOrDefaultAsync(r => r.UserId == userId
                && r.RequestDate.Date == today
                && r.Status == "Approved");

            if (approvedRequest != null)
            {
                log.DayStatus = approvedRequest.RequestType; // "WFH" or "HalfDay"
                approvedRequest.DailyLogId = log.Id;
                await _db.SaveChangesAsync();
            }
            return await MapToDtoAsync(log.Id);
        }

        public async Task<DailyLogResponseDto?> CheckOutAsync(int userId, CheckOutDto dto)
        {
            var today = DateTime.UtcNow.Date;
            var log = await _db.DailyLogs
                .Include(d => d.BreakLogs)
                .FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today);

            if (log == null || log.CheckInTime == null) return null;

            // End any active breaks
            var activeBreak = log.BreakLogs.FirstOrDefault(b => b.IsActive);
            if (activeBreak != null)
            {
                activeBreak.EndTime = DateTime.UtcNow;
                activeBreak.DurationMinutes = (int)(DateTime.UtcNow - activeBreak.StartTime).TotalMinutes;
                activeBreak.IsActive = false;
            }

            log.CheckOutTime = DateTime.UtcNow;
            log.TotalBreakMinutes = log.BreakLogs.Sum(b => b.DurationMinutes);
            var totalElapsed = (int)(log.CheckOutTime.Value - log.CheckInTime.Value).TotalMinutes;
            log.TotalWorkMinutes = Math.Max(0, totalElapsed - log.TotalBreakMinutes);

            if (dto.Notes != null) log.Notes = dto.Notes;

            await _db.SaveChangesAsync();
            return await MapToDtoAsync(log.Id);
        }

        public async Task<DailyLogResponseDto?> GetTodayLogAsync(int userId)
        {
            var today = DateTime.UtcNow.Date;
            var log = await _db.DailyLogs.FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today);
            if (log == null) return null;
            return await MapToDtoAsync(log.Id);
        }

        public async Task<DailyLogResponseDto?> GetLogByDateAsync(int userId, DateTime date)
        {
            var log = await _db.DailyLogs.FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == date.Date);
            if (log == null) return null;
            return await MapToDtoAsync(log.Id);
        }

        public async Task<List<DailyLogResponseDto>> GetHistoryAsync(int userId, int days = 30)
        {
            var from = DateTime.UtcNow.Date.AddDays(-days);
            var logs = await _db.DailyLogs
                .Where(d => d.UserId == userId && d.LogDate >= from)
                .OrderByDescending(d => d.LogDate)
                .ToListAsync();

            var result = new List<DailyLogResponseDto>();
            foreach (var log in logs)
                result.Add(await MapToDtoAsync(log.Id));

            return result;
        }

        private async Task<DailyLogResponseDto> MapToDtoAsync(int logId)
        {
            var log = await _db.DailyLogs
                .Include(d => d.BreakLogs)
                .Include(d => d.TaskLogs)
                .Include(d => d.SupportLogs).ThenInclude(s => s.SupportedDeveloper)
                .Include(d => d.SupportLogs).ThenInclude(s => s.MediaEvidences)
                .FirstAsync(d => d.Id == logId);

            var workMins = log.TotalWorkMinutes;

            // If still checked in (no checkout), compute live
            if (log.CheckInTime != null && log.CheckOutTime == null)
            {
                var elapsed = (int)(DateTime.UtcNow - log.CheckInTime.Value).TotalMinutes;
                var breakMins = log.BreakLogs.Where(b => !b.IsActive).Sum(b => b.DurationMinutes);
                var activeBreak = log.BreakLogs.FirstOrDefault(b => b.IsActive);
                if (activeBreak != null)
                    breakMins += (int)(DateTime.UtcNow - activeBreak.StartTime).TotalMinutes;
                workMins = Math.Max(0, elapsed - breakMins);
            }

            return new DailyLogResponseDto
            {
                Id = log.Id,
                LogDate = log.LogDate,
                CheckInTime = log.CheckInTime,
                CheckOutTime = log.CheckOutTime,
                TotalWorkMinutes = workMins,
                TotalBreakMinutes = log.BreakLogs.Sum(b => b.DurationMinutes),
                DayStatus = log.DayStatus,
                Notes = log.Notes,
                WorkHours = FormatMinutes(workMins),
                Breaks = log.BreakLogs.Select(b => new BreakLogDto
                {
                    Id = b.Id,
                    BreakType = b.BreakType,
                    StartTime = b.StartTime,
                    EndTime = b.EndTime,
                    DurationMinutes = b.IsActive
                        ? (int)(DateTime.UtcNow - b.StartTime).TotalMinutes
                        : b.DurationMinutes,
                    IsActive = b.IsActive
                }).ToList(),
                Tasks = log.TaskLogs.Select(t => new TaskLogDto
                {
                    Id = t.Id,
                    TaskTitle = t.TaskTitle,
                    Description = t.Description,
                    ProjectName = t.ProjectName,
                    Status = t.Status,
                    TimeSpentMinutes = t.TimeSpentMinutes,
                    Priority = t.Priority,
                    Tags = t.Tags,
                    CompletedAt = t.CompletedAt,
                    CreatedAt = t.CreatedAt
                }).ToList(),
                SupportLogs = log.SupportLogs.Select(s => new SupportLogResponseDto
                {
                    Id = s.Id,
                    SupportedDeveloperName = s.SupportedDeveloper.FullName,
                    IssueDescription = s.IssueDescription,
                    Resolution = s.Resolution,
                    TimeSpentMinutes = s.TimeSpentMinutes,
                    SupportType = s.SupportType,
                    SupportedAt = s.SupportedAt,
                    Media = s.MediaEvidences.Select(m => new MediaEvidenceDto
                    {
                        Id = m.Id,
                        MediaType = m.MediaType,
                        FileName = m.FileName,
                        Url = $"/api/support/media/{m.Id}",
                        FileSizeBytes = m.FileSizeBytes,
                        MimeType = m.MimeType
                    }).ToList()
                }).ToList()
            };
        }

        private static string FormatMinutes(int minutes)
        {
            var h = minutes / 60;
            var m = minutes % 60;
            return $"{h}h {m}m";
        }
    }
}