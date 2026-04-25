// Created: 2026-01-09 - Service interface for Guide Calculator
using Email.Models.GuideCalculator;
using Email.Models.Reports;

namespace Email.Services
{
    /// <summary>
    /// Calculates what a guide owes or is owed for a single day.
    /// </summary>
    public interface IGuideCalculatorService
    {
        Task<GuideCalculationResult> CalculateForGuideAsync(
            int guideId,
            DateTime tourDate,
            CancellationToken ct = default);

        Task<GuideCalculationResult> CalculateFromReportAsync(TourReport report);
    }
}
