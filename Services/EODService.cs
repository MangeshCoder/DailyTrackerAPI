using DailyTrackerAPI.Data;
using DailyTrackerAPI.DTOs;
using DailyTrackerAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DailyTrackerAPI.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Feature 6: EOD Report Service
    // ─────────────────────────────────────────────────────────────────────────
    public interface IEODService
    {
        Task<EODReportResponseDto> SubmitReportAsync(int userId, CreateEODReportDto dto);
        Task<EODReportResponseDto?> GetTodayReportAsync(int userId);
        Task<List<EODReportResponseDto>> GetUserReportsAsync(int userId, int days = 14);
        Task<List<EODReportResponseDto>> GetAllPendingReviewsAsync();  // Manager
        Task ReviewReportAsync(int reportId, int managerId, ManagerReviewEODDto dto);
    }

    public class EODService : IEODService
    {
        private readonly AppDbContext _db;

        public EODService(AppDbContext db) { _db = db; }

        public async Task<EODReportResponseDto> SubmitReportAsync(int userId, CreateEODReportDto dto)
        {
            var today = DateTime.UtcNow.Date;
            var log = await _db.DailyLogs.FirstOrDefaultAsync(d => d.UserId == userId && d.LogDate == today)
                ?? throw new InvalidOperationException("You must check in before submitting EOD report.");

            // Update or create
            var existing = await _db.EODReports.FirstOrDefaultAsync(r => r.UserId == userId && r.ReportDate == today);

            if (existing != null)
            {
                existing.WhatWasDone = dto.WhatWasDone;
                existing.Blockers = dto.Blockers;
                existing.PlanForTomorrow = dto.PlanForTomorrow;
                existing.Learnings = dto.Learnings;
                existing.MoodRating = dto.MoodRating;
                existing.SubmittedAt = DateTime.UtcNow;
            }
            else
            {
                existing = new EODReport
                {
                    UserId = userId,
                    DailyLogId = log.Id,
                    ReportDate = today,
                    WhatWasDone = dto.WhatWasDone,
                    Blockers = dto.Blockers,
                    PlanForTomorrow = dto.PlanForTomorrow,
                    Learnings = dto.Learnings,
                    MoodRating = dto.MoodRating
                };
                _db.EODReports.Add(existing);
            }

            await _db.SaveChangesAsync();
            return await MapReport(existing);
        }

        public async Task<EODReportResponseDto?> GetTodayReportAsync(int userId)
        {
            var today = DateTime.UtcNow.Date;
            var report = await _db.EODReports
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.UserId == userId && r.ReportDate == today);

            return report == null ? null : await MapReport(report);
        }

        public async Task<List<EODReportResponseDto>> GetUserReportsAsync(int userId, int days = 14)
        {
            var from = DateTime.UtcNow.Date.AddDays(-days);
            var reports = await _db.EODReports
                .Include(r => r.User)
                .Where(r => r.UserId == userId && r.ReportDate >= from)
                .OrderByDescending(r => r.ReportDate)
                .ToListAsync();

            var result = new List<EODReportResponseDto>();
            foreach (var r in reports) result.Add(await MapReport(r));
            return result;
        }

        public async Task<List<EODReportResponseDto>> GetAllPendingReviewsAsync()
        {
            var reports = await _db.EODReports
                .Include(r => r.User)
                .Where(r => !r.IsReviewedByManager)
                .OrderByDescending(r => r.ReportDate)
                .ToListAsync();

            var result = new List<EODReportResponseDto>();
            foreach (var r in reports) result.Add(await MapReport(r));
            return result;
        }

        public async Task ReviewReportAsync(int reportId, int managerId, ManagerReviewEODDto dto)
        {
            var report = await _db.EODReports.FindAsync(reportId)
                ?? throw new KeyNotFoundException("Report not found.");

            report.IsReviewedByManager = true;
            report.ManagerComment = dto.ManagerComment;
            await _db.SaveChangesAsync();
        }

        private static Task<EODReportResponseDto> MapReport(EODReport r) =>
            Task.FromResult(new EODReportResponseDto
            {
                Id = r.Id,
                UserName = r.User?.FullName ?? "Unknown",
                ReportDate = r.ReportDate,
                WhatWasDone = r.WhatWasDone,
                Blockers = r.Blockers,
                PlanForTomorrow = r.PlanForTomorrow,
                Learnings = r.Learnings,
                MoodRating = r.MoodRating,
                SubmittedAt = r.SubmittedAt,
                IsReviewedByManager = r.IsReviewedByManager,
                ManagerComment = r.ManagerComment
            });
    }
}
