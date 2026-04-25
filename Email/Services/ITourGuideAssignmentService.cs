using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-01-08 - Service contract for per-tour-instance guide assignments
    /// </summary>
    public interface ITourGuideAssignmentService
    {
        Task<List<TourGuideAssignment>> GetAssignmentsForDateAsync(DateTime date, CancellationToken ct = default);
        Task<TourGuideAssignment?> GetAssignmentAsync(DateTime date, string tourName, string tourTime, CancellationToken ct = default);
        Task<TourGuideAssignment> SaveAssignmentAsync(TourGuideAssignment assignment, CancellationToken ct = default);
    }
}
