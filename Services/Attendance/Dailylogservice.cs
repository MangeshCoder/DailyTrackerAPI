// ─────────────────────────────────────────────────────────────────────────────
//  FILE 3: backend/Services/Dailylogservice.cs
//  ACTION: REPLACE entire file
//
//  Key changes:
//  1. ILocationService injected
//  2. CheckInAsync — location logic:
//       Step A: Check if employee has approved WFH for today
//               YES → skip location check, set DayStatus = "WFH"
//               NO  → run location check, throw LocationException if outside
//  3. CheckOutAsync — same logic:
//       WFH approved → skip location check
//       No WFH       → location check mandatory
//  4. Both store coordinates in the DailyLog record
// ─────────────────────────────────────────────────────────────────────────────

using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models;
using DailyTrackerAPI.Models.Tasks;
using DailyTrackerAPI.Services.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Services.Attendance
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
        private readonly ILocationService _location;

        public DailyLogService(AppDbContext db, ILocationService location)
        {
            _db = db;
            _location = location;
        }

        public async Task<DailyLogResponseDto?> CheckInAsync(int userId, CheckInDto dto)
        {
            var today = DateTime.UtcNow.Date;

            // Already checked in today — return existing log
            var existing = await _db.DailyLogs
                .FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today);
            if (existing != null) return await MapToDtoAsync(existing.Id);

            // ── STEP 1: Check for approved WFH request for today ──────────────
            //
            // If approved WFH exists → employee is working from home legitimately
            // → skip location check entirely
            // → set DayStatus to "WFH" automatically
            //
            var approvedWFH = await _db.WFHRequests
                .FirstOrDefaultAsync(r =>
                    r.UserId == userId &&
                    r.RequestDate.Date == today &&
                    r.Status == "Approved" &&
                    r.RequestType == "WFH");

            var isWFH = approvedWFH != null;

            // ── STEP 2: Location check (only if NOT on approved WFH) ──────────
            if (!isWFH)
            {
                var distance = _location.GetDistanceFromOffice(dto.Latitude, dto.Longitude);

                if (!_location.IsWithinOffice(dto.Latitude, dto.Longitude))
                {
                    throw new LocationException(
                        $"You are not at the office. " +
                        $"You must be within {_location.RadiusMetres:0}m of {_location.OfficeName} to check in. " +
                        $"Your current distance: {distance:0}m. " +
                        $"If you are working from home, please apply for a WFH request first."
                    );
                }
            }

            // ── STEP 3: Create the daily log ──────────────────────────────────
            var log = new DailyLog
            {
                UserId = userId,
                LogDate = today,
                CheckInTime = DateTime.UtcNow,
                DayStatus = isWFH ? "WFH" : "Present",
                Notes = dto.Notes,
                CheckInLatitude = isWFH ? null : dto.Latitude,
                CheckInLongitude = isWFH ? null : dto.Longitude,
            };

            _db.DailyLogs.Add(log);
            await _db.SaveChangesAsync();

            // Link WFH request to this daily log
            if (approvedWFH != null)
            {
                approvedWFH.DailyLogId = log.Id;
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

            // ── STEP 1: Check for approved WFH ───────────────────────────────
            var isWFH = await _db.WFHRequests
                .AnyAsync(r =>
                    r.UserId == userId &&
                    r.RequestDate.Date == today &&
                    r.Status == "Approved" &&
                    r.RequestType == "WFH");

            // ── STEP 2: Location check (only if NOT on approved WFH) ─────────
            if (!isWFH)
            {
                var distance = _location.GetDistanceFromOffice(dto.Latitude, dto.Longitude);

                if (!_location.IsWithinOffice(dto.Latitude, dto.Longitude))
                {
                    throw new LocationException(
                        $"You are not at the office. " +
                        $"You must be within {_location.RadiusMetres:0}m of {_location.OfficeName} to check out. " +
                        $"Your current distance: {distance:0}m."
                    );
                }

                // Store check-out coordinates
                log.CheckOutLatitude = dto.Latitude;
                log.CheckOutLongitude = dto.Longitude;
            }

            // ── STEP 3: End active break if any ──────────────────────────────
            var activeBreak = log.BreakLogs.FirstOrDefault(b => b.IsActive);
            if (activeBreak != null)
            {
                activeBreak.EndTime = DateTime.UtcNow;
                activeBreak.DurationMinutes = (int)(DateTime.UtcNow - activeBreak.StartTime).TotalMinutes;
                activeBreak.IsActive = false;
            }

            // ── STEP 4: Calculate work time ───────────────────────────────────
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