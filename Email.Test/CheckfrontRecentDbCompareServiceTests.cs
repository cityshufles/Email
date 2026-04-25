using Email.Models;
using Email.Services;
using Email.Services.GmailProcessing;
using Email.Services.GmailProcessing.Dtos;
using Email.Services.GmailProcessing.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using PipelineBooking = Email.Services.GmailProcessing.Models.Booking;

namespace Email.Test;

[TestFixture]
public class CheckfrontRecentDbCompareServiceTests
{
    [Test]
    public async Task CompareRecentAsync_ComputesMissingAndMismatches_AndPerformsNoWrites()
    {
        var api = new StubCheckfrontReadOnlyApi(new Dictionary<int, List<CheckfrontBooking>>
        {
            [1] = new()
            {
                new CheckfrontBooking
                {
                    Code = "BR-1001",
                    CustomerName = "John Walker",
                    StartDateRaw = "20260320",
                    StartTimeRaw = "09:30",
                    Total = "5000",
                    NumberOfAttendees = 2,
                    Status = "PAID",
                    StatusName = "Confirmed",
                    StatusId = "PAID"
                },
                new CheckfrontBooking
                {
                    Code = "BR-1002",
                    CustomerName = "Missing Person",
                    StartDateRaw = "20260321",
                    StartTimeRaw = "10:30",
                    NumberOfAttendees = 1,
                    Status = "PAID",
                    StatusName = "Confirmed",
                    StatusId = "PAID"
                },
                new CheckfrontBooking
                {
                    Code = "BR-1003",
                    CustomerName = "Mismatch Name",
                    StartDateRaw = "20260322",
                    StartTimeRaw = "11:30",
                    NumberOfAttendees = 3,
                    Status = "CANCELLED",
                    StatusName = "Cancelled",
                    StatusId = "CANCELLED"
                }
            },
            [2] = new()
        });

        var repo = new StubBookingRepository(new List<PipelineBooking>
        {
            new PipelineBooking
            {
                BookingCode = "BR-1001",
                CustomerName = "John Walker",
                TourDate = new DateTime(2026, 3, 20),
                TourTime = "09:30",
                NumberOfAttendees = 2,
                IsActive = true,
                BookingStatus = "Confirmed",
                UpdatedAt = DateTime.UtcNow
            },
            new PipelineBooking
            {
                BookingCode = "BR-1003",
                CustomerName = "Other Name",
                TourDate = new DateTime(2026, 3, 22),
                TourTime = "08:00",
                NumberOfAttendees = 1,
                IsActive = true,
                BookingStatus = "Confirmed",
                UpdatedAt = DateTime.UtcNow
            },
            new PipelineBooking
            {
                Id = 999,
                BookingCode = "GYG-9000",
                VendorName = "GetYourGuide",
                BookingAmount = 1200m,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            }
        });

        var sut = new CheckfrontRecentDbCompareService(
            api,
            repo,
            NullLogger<CheckfrontRecentDbCompareService>.Instance);

        var result = await sut.CompareRecentAsync(daysBack: 30, limitPerPage: 50, maxPages: 5, CancellationToken.None);

        Assert.That(result.CheckfrontCount, Is.EqualTo(3));
        Assert.That(result.DbCount, Is.EqualTo(2));
        Assert.That(result.MissingInDb, Is.EquivalentTo(new[] { "BR-1002" }));
        Assert.That(result.PresentInDb, Is.EquivalentTo(new[] { "BR-1001", "BR-1003" }));
        Assert.That(result.FieldMismatches.Any(m => m.BookingCode == "BR-1003" && m.Field == "CustomerName"), Is.True);
        Assert.That(result.FieldMismatches.Any(m => m.BookingCode == "BR-1003" && m.Field == "TourTime"), Is.True);
        Assert.That(result.FieldMismatches.Any(m => m.BookingCode == "BR-1003" && m.Field == "Attendees"), Is.True);
        Assert.That(result.FieldMismatches.Any(m => m.BookingCode == "BR-1003" && m.Field == "Status"), Is.True);
        Assert.That(result.Outcome, Is.EqualTo("Mismatch"));
        Assert.That(result.CheckfrontAmountCandidateCount, Is.EqualTo(1));
        Assert.That(result.CheckfrontAmountCandidates.Any(c =>
            c.BookingCode == "BR-1001" &&
            c.RawTotal == 5000m &&
            c.NormalizedTotal == 50m &&
            c.DbBookingAmount == null &&
            c.SuggestedAmount == 50m), Is.True);
        Assert.That(result.GlobalHighAmountCandidateCount, Is.EqualTo(1));
        Assert.That(result.GlobalHighAmountCandidates.Any(c => c.BookingCode == "GYG-9000" && c.BookingAmount == 1200m), Is.True);

        Assert.That(repo.CreateCalls, Is.EqualTo(0));
        Assert.That(repo.UpdateCalls, Is.EqualTo(0));
        Assert.That(repo.DeleteCalls, Is.EqualTo(0));
        Assert.That(repo.CancelCalls, Is.EqualTo(0));
        Assert.That(repo.DeactivateCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task CompareRecentAsync_NoApiData_ReturnsNoApiDataOutcome()
    {
        var api = new StubCheckfrontReadOnlyApi(new Dictionary<int, List<CheckfrontBooking>>
        {
            [1] = new()
        });

        var repo = new StubBookingRepository(new List<PipelineBooking>());
        var sut = new CheckfrontRecentDbCompareService(
            api,
            repo,
            NullLogger<CheckfrontRecentDbCompareService>.Instance);

        var result = await sut.CompareRecentAsync(30, 50, 5, CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo("NoApiData"));
        Assert.That(result.CheckfrontCount, Is.EqualTo(0));
        Assert.That(result.DbCount, Is.EqualTo(0));
        Assert.That(result.CheckfrontAmountCandidateCount, Is.EqualTo(0));
        Assert.That(result.GlobalHighAmountCandidateCount, Is.EqualTo(0));
    }

    [Test]
    public async Task CompareRecentAsync_GlobalHighAmountReport_RespectsThresholdDaysBackAndScanLimit()
    {
        var api = new StubCheckfrontReadOnlyApi(new Dictionary<int, List<CheckfrontBooking>>
        {
            [1] = new()
            {
                new CheckfrontBooking
                {
                    Code = "BR-2001",
                    CustomerName = "Scan Limit Example",
                    StartDateRaw = "20260320",
                    StartTimeRaw = "09:30",
                    Total = "50.00",
                    NumberOfAttendees = 2,
                    Status = "PAID",
                    StatusName = "Confirmed",
                    StatusId = "PAID"
                }
            },
            [2] = new()
        });

        var repo = new StubBookingRepository(new List<PipelineBooking>
        {
            new PipelineBooking
            {
                Id = 1,
                BookingCode = "BR-2001",
                VendorName = "Checkfront",
                CustomerName = "Scan Limit Example",
                TourDate = new DateTime(2026, 3, 20),
                TourTime = "09:30",
                NumberOfAttendees = 2,
                BookingAmount = 50m,
                Currency = "USD",
                IsActive = true,
                BookingStatus = "Confirmed",
                UpdatedAt = DateTime.UtcNow
            },
            new PipelineBooking
            {
                Id = 2,
                BookingCode = "HIGH-RECENT-1",
                VendorName = "Viator",
                BookingAmount = 1000m,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            },
            new PipelineBooking
            {
                Id = 3,
                BookingCode = "HIGH-OLD-EXCLUDED",
                VendorName = "GetYourGuide",
                BookingAmount = 350m,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow.AddDays(-45)
            },
            new PipelineBooking
            {
                Id = 4,
                BookingCode = "HIGH-RECENT-BUT-OUTSIDE-SCANLIMIT",
                VendorName = "Checkfront",
                BookingAmount = 900m,
                Currency = "USD",
                UpdatedAt = DateTime.UtcNow
            }
        });

        var sut = new CheckfrontRecentDbCompareService(
            api,
            repo,
            NullLogger<CheckfrontRecentDbCompareService>.Instance);

        var result = await sut.CompareRecentAsync(
            daysBack: 30,
            limitPerPage: 50,
            maxPages: 5,
            ct: CancellationToken.None,
            options: new CheckfrontRecentDbCompareOptions
            {
                HighAmountThresholdUsd = 300m,
                GlobalScanLimit = 2,
                IncludeGlobalHighAmountReport = true,
                CheckfrontMinorUnitMinRaw = 100m
            });

        Assert.That(result.CheckfrontAmountCandidateCount, Is.EqualTo(0));
        Assert.That(result.GlobalHighAmountCandidateCount, Is.EqualTo(1));
        Assert.That(result.GlobalHighAmountCandidates[0].BookingCode, Is.EqualTo("HIGH-RECENT-1"));
        Assert.That(result.GlobalHighAmountCandidates[0].BookingAmount, Is.EqualTo(1000m));

        Assert.That(repo.CreateCalls, Is.EqualTo(0));
        Assert.That(repo.UpdateCalls, Is.EqualTo(0));
        Assert.That(repo.DeleteCalls, Is.EqualTo(0));
        Assert.That(repo.CancelCalls, Is.EqualTo(0));
        Assert.That(repo.DeactivateCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task CompareRecentAsync_MinorUnitScaling_IsSuppressedWhenAttendeeSanityFails()
    {
        var api = new StubCheckfrontReadOnlyApi(new Dictionary<int, List<CheckfrontBooking>>
        {
            [1] = new()
            {
                new CheckfrontBooking
                {
                    Code = "YHCH-300325",
                    CustomerName = "Large Group",
                    StartDateRaw = "20260325",
                    StartTimeRaw = "09:30",
                    Total = "234",
                    NumberOfAttendees = 0,
                    Status = "PAID",
                    StatusName = "Confirmed",
                    StatusId = "PAID"
                }
            },
            [2] = new()
        });

        var repo = new StubBookingRepository(new List<PipelineBooking>
        {
            new PipelineBooking
            {
                BookingCode = "YHCH-300325",
                CustomerName = "Large Group",
                TourDate = new DateTime(2026, 3, 25),
                TourTime = "09:30",
                NumberOfAttendees = 47,
                NumberOfAdults = 47,
                NumberOfChildren = 0,
                BookingAmount = null,
                Currency = "USD",
                IsActive = true,
                BookingStatus = "Confirmed",
                UpdatedAt = DateTime.UtcNow
            }
        });

        var sut = new CheckfrontRecentDbCompareService(
            api,
            repo,
            NullLogger<CheckfrontRecentDbCompareService>.Instance);

        var result = await sut.CompareRecentAsync(
            daysBack: 30,
            limitPerPage: 50,
            maxPages: 5,
            ct: CancellationToken.None,
            options: new CheckfrontRecentDbCompareOptions
            {
                HighAmountThresholdUsd = 500m,
                GlobalScanLimit = 5000,
                IncludeGlobalHighAmountReport = true,
                CheckfrontMinorUnitMinRaw = 100m
            });

        var candidate = result.CheckfrontAmountCandidates.Single(c => c.BookingCode == "YHCH-300325");
        Assert.That(candidate.RawTotal, Is.EqualTo(234m));
        Assert.That(candidate.NormalizedTotal, Is.EqualTo(234m), "Should keep unscaled amount when scaled value is not plausible for attendee count.");
        Assert.That(candidate.SuggestedAmount, Is.EqualTo(234m));
        Assert.That(candidate.Reason, Does.Contain("did not pass attendee/amount sanity"));
    }

    private sealed class StubCheckfrontReadOnlyApi : ICheckfrontReadOnlyApi
    {
        private readonly Dictionary<int, List<CheckfrontBooking>> _pages;

        public StubCheckfrontReadOnlyApi(Dictionary<int, List<CheckfrontBooking>> pages)
        {
            _pages = pages;
        }

        public string ActiveEndpoint => "https://example.checkfront.test/api/4.0/";

        public Task<CheckfrontConnectionTest> TestConnectionAsync()
            => Task.FromResult(new CheckfrontConnectionTest { IsConnected = true, Message = "ok" });

        public Task<CheckfrontV4BookingsListResponse> ListV4BookingsAsync(int limit = 25, int offset = 0)
            => Task.FromResult(new CheckfrontV4BookingsListResponse());

        public Task<CheckfrontBooking?> GetBookingByCodeOrIdAsync(string codeOrId)
            => Task.FromResult<CheckfrontBooking?>(null);

        public Task<List<CheckfrontV4BookingNote>> GetBookingNotesByCodeOrIdAsync(string codeOrId)
            => Task.FromResult(new List<CheckfrontV4BookingNote>());

        public Task<List<CheckfrontBooking>> GetBookingsAsync(DateTime? startDate = null, DateTime? endDate = null, string? status = null, int? limit = null, int? page = null)
        {
            var resolvedPage = page ?? 1;
            return Task.FromResult(_pages.TryGetValue(resolvedPage, out var rows) ? rows : new List<CheckfrontBooking>());
        }

        public Task<List<CheckfrontCustomerMatch>> SearchCustomersByEmailAsync(string emailAddress)
            => Task.FromResult(new List<CheckfrontCustomerMatch>());

        public Task<List<CheckfrontCustomerMatch>> SearchCustomersByNameAsync(string customerName)
            => Task.FromResult(new List<CheckfrontCustomerMatch>());

        public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(string customerId, int limit = 25, int page = 1)
            => Task.FromResult(new List<CheckfrontBooking>());

        public Task<List<CheckfrontBooking>> GetBookingsByCustomerIdAsync(int customerId, int limit = 25, int page = 1)
            => Task.FromResult(new List<CheckfrontBooking>());
    }

    private sealed class StubBookingRepository : IBookingRepository
    {
        private readonly IReadOnlyList<PipelineBooking> _rows;

        public StubBookingRepository(IReadOnlyList<PipelineBooking> rows)
        {
            _rows = rows;
        }

        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int DeactivateCalls { get; private set; }

        public Task<PipelineBooking?> FindByCodeAsync(string bookingCode, CancellationToken cancellationToken)
            => Task.FromResult(_rows.FirstOrDefault(x => string.Equals(x.BookingCode, bookingCode, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<PipelineBooking>> GetByCodesAsync(IReadOnlyList<string> bookingCodes, CancellationToken cancellationToken)
        {
            var normalized = new HashSet<string>(bookingCodes, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<PipelineBooking> matches = _rows.Where(x => normalized.Contains(x.BookingCode)).ToList();
            return Task.FromResult(matches);
        }

        public Task<int> CreateAsync(PipelineBooking booking, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(1);
        }

        public Task UpdateAsync(PipelineBooking booking, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task DeactivateOriginalOnModificationAsync(string originalBookingCode, string newBookingCode, CancellationToken cancellationToken)
        {
            DeactivateCalls++;
            return Task.CompletedTask;
        }

        public Task<int> CancelAsync(string bookingCode, string? cancellationReason, CancellationToken cancellationToken)
        {
            CancelCalls++;
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<PipelineBooking>> GetRecentAsync(int limit, CancellationToken cancellationToken)
            => Task.FromResult(_rows.Take(limit).ToList() as IReadOnlyList<PipelineBooking>);

        public Task DeleteAsync(IReadOnlyList<int> bookingIds, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }
}
