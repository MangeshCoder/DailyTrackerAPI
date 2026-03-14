using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models.Tasks;
using DailyTrackerAPI.Services.Team;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Services.Tasks
{
    // ── Custom exception for location failures ────────────────────────────────
    public class LocationException : Exception
    {
        public LocationException(string message) : base(message) { }
    }

    public interface ISupportService
    {
        Task<SupportLogResponseDto?> CreateSupportAsync(int userId, CreateSupportDto dto);
        Task<SupportLogResponseDto?> CreateSupportWithMediaAsync(int userId, CreateSupportDto dto, IFormFileCollection? files);
        Task<bool> DeleteSupportAsync(int userId, int supportId);
        Task<List<SupportLogResponseDto>> GetTodaySupportAsync(int userId);
    }

    public class SupportService : ISupportService
    {
        private readonly AppDbContext _db;
        private readonly IMediaStorageService _mediaStorage;
        private readonly ILocationService _location;

        public SupportService(AppDbContext db, IMediaStorageService mediaStorage, ILocationService location)
        {
            _db = db;
            _mediaStorage = mediaStorage;
            _location = location;
        }

        public async Task<SupportLogResponseDto?> CreateSupportAsync(
           int userId, CreateSupportDto dto)
        {
            // ── STEP 1: Location check (backend enforcer) ─────────────────────
            //
            // Even though the frontend already checked, we ALWAYS verify here.
            // This is the real security gate — a client-side check alone can be
            // bypassed using DevTools, Postman, or a modified JS bundle.
            //
            var distance = _location.GetDistanceFromOffice(dto.Latitude, dto.Longitude);

            if (!_location.IsWithinOffice(dto.Latitude, dto.Longitude))
            {
                throw new LocationException(
                    $"You are not at your company location. " +
                    $"You must be within {_location.RadiusMetres:0}m of {_location.OfficeName} to log support. " +
                    $"Your current distance: {distance:0}m."
                );
            }

            // ── STEP 2: Standard validation ───────────────────────────────────
            var today = DateTime.UtcNow.Date;
            var log = await _db.DailyLogs
                .FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today);
            if (log == null) return null;

            var developer = await _db.Users.FindAsync(dto.SupportedDeveloperId);
            if (developer == null) return null;

            // ── STEP 3: Save with coordinates ─────────────────────────────────
            var support = new SupportLog
            {
                DailyLogId = log.Id,
                SupportedDeveloperId = dto.SupportedDeveloperId,
                IssueDescription = dto.IssueDescription,
                Resolution = dto.Resolution,
                TimeSpentMinutes = dto.TimeSpentMinutes,
                SupportType = dto.SupportType,
                SupportedAt = DateTime.UtcNow,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                DistanceFromOfficeMetres = distance
            };

            _db.SupportLogs.Add(support);
            await _db.SaveChangesAsync();

            return MapToDto(support, developer.FullName, new List<MediaEvidenceDto>());
        }

        public async Task<SupportLogResponseDto?> CreateSupportWithMediaAsync(
            int userId, CreateSupportDto dto, IFormFileCollection? files)
        {
            // Location check happens inside CreateSupportAsync — no duplicate code
            var result = await CreateSupportAsync(userId, dto);

            if (result == null || files == null || files.Count == 0)
                return result;

            var mediaList = await _mediaStorage.SaveSupportMediaAsync(userId, result.Id, files);
            result.Media = mediaList;
            return result;
        }

        private static SupportLogResponseDto MapToDto(SupportLog s, string developerName, List<MediaEvidenceDto> media) =>
            new()
            {
                Id = s.Id,
                SupportedDeveloperName = developerName,
                IssueDescription = s.IssueDescription,
                Resolution = s.Resolution,
                TimeSpentMinutes = s.TimeSpentMinutes,
                SupportType = s.SupportType,
                SupportedAt = s.SupportedAt,
                Media = media,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                DistanceFromOfficeMetres = s.DistanceFromOfficeMetres
            };

        public async Task<bool> DeleteSupportAsync(int userId, int supportId)
        {
            var support = await _db.SupportLogs
                .Include(s => s.DailyLog)
                .FirstOrDefaultAsync(s => s.Id == supportId && s.DailyLog.UserId == userId);

            if (support == null) return false;

            _db.SupportLogs.Remove(support);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<SupportLogResponseDto>> GetTodaySupportAsync(int userId)
        {
            var today = DateTime.UtcNow.Date;
            var list = await _db.SupportLogs
                .Include(s => s.DailyLog)
                .Include(s => s.SupportedDeveloper)
                .Include(s => s.MediaEvidences)
                .Where(s => s.DailyLog.UserId == userId && s.DailyLog.LogDate == today)
                .ToListAsync();

            return list.Select(s => MapToDto(
                s,
                s.SupportedDeveloper.FullName,
                s.MediaEvidences.Select(m => new MediaEvidenceDto
                {
                    Id = m.Id,
                    MediaType = m.MediaType,
                    FileName = m.FileName,
                    Url = _mediaStorage.GetSecuredMediaUrl(m.Id),
                    FileSizeBytes = m.FileSizeBytes,
                    MimeType = m.MimeType
                }).ToList()
            )).ToList();
        }
    }
}
