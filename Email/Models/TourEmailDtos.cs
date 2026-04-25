using System;
using System.Collections.Generic;

namespace Email.Models
{
    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class ProcessRequest
    {
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class ProcessCustomSpanRequest
    {
        public string CollectionSpan { get; set; } = "Today";
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class TourEmailsProcessingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int TotalCollected { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalInserted { get; set; }
        public int TotalClassified { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class TourEmailsApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class LatestInboxEmailDto
    {
        public int Id { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public DateTime? ReceivedDate { get; set; }
        public DateTime? CollectedAt { get; set; }
        public bool ManualParsingCompleted { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class CollectionSummaryRowDto
    {
        public string RowType { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public DateTime? InboxReceivedDate { get; set; }
        public DateTime? InboxUpdatedAt { get; set; }
        public string? InboxSubject { get; set; }
        public string? InboxManualParsedEmailType { get; set; }
        public string? ProcessedEmailType { get; set; }
        public string? ProcessedStatus { get; set; }
        public string? ProcessedBookingCode { get; set; }
        public string? ProcessedCustomerName { get; set; }
        public string? ProcessedTourName { get; set; }
        public DateTime? ProcessedTourDate { get; set; }
        public string? ProcessedTourTime { get; set; }
        public string? PreviousBookingCode { get; set; }
        public string? BookingEmailType { get; set; }
        public bool? BookingIsCancellation { get; set; }
        public bool? BookingIsModification { get; set; }
        public bool? BookingIsConfirmation { get; set; }
        public bool? BookingIsActive { get; set; }
        public string? BookingTourName { get; set; }
        public DateTime? BookingTourDate { get; set; }
        public string? BookingTourTime { get; set; }
        public int? BookingNumberOfAttendees { get; set; }
        public string? BookingStatus { get; set; }
        public DateTime? BookingCreatedAt { get; set; }
        public DateTime? BookingUpdatedAt { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class CollectionResult
    {
        public int TotalCollected { get; set; }
        public int TotalSkipped { get; set; }
        public int TotalErrors { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class CollectionStatusDto
    {
        public int TotalCollected { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalUnprocessed { get; set; }
        public DateTime? LastCollectionDate { get; set; }
        public DateTime? LastProcessingDate { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class UnprocessedEmailDto
    {
        public int Id { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string? VendorName { get; set; }
        public string? EmailType { get; set; }
        public DateTime CollectedAt { get; set; }
        public string TextBodyPreview { get; set; } = string.Empty;
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class ProcessSpecificEmailsRequest
    {
        public List<int> EmailIds { get; set; } = new();
        public bool CreateReport { get; set; } = false;
        public string? ReportPath { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class InboxEmailDetailDto
    {
        public int Id { get; set; }
        public long Uid { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string TextBody { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;
        public string TextBodyPreview { get; set; } = string.Empty;
        public int AttachmentCount { get; set; }
        public string AttachmentNames { get; set; } = string.Empty;
        public DateTime CollectedAt { get; set; }
        public string CollectionBatchId { get; set; } = string.Empty;
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class TourProcessedEmailFromTourEmailLibModel
    {
        public int Id { get; set; }
        public int InboxEmailId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public bool IsTourBookingEmail { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsBooking { get; set; }
        public int? ClassificationRuleId { get; set; }
        public string ProcessingStatus { get; set; } = "pending";
        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? ProcessingCompletedAt { get; set; }
        public string? ProcessingError { get; set; }
        public int ProcessingAttempts { get; set; }
        public DateTime? NextProcessingAttempt { get; set; }
        public DateTime? RateLimitResetAt { get; set; }
        public string? CustomerName { get; set; }
        public string? BookingCode { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public int? NumberOfAttendees { get; set; }
        public int? NumberOfAdults { get; set; }
        public int? NumberOfChildren { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        public string? TourLocation { get; set; }
        public string? Language { get; set; }
        public string? CustomerIdentifier { get; set; }
        public bool IsLatestAction { get; set; } = true;
        public string? PlainTextContent { get; set; }
        public string? HtmlContent { get; set; }
        public string? BookingAlterationNotes { get; set; }
        public string? ExtractedBookingCode { get; set; }
        public string? NewBookingCode { get; set; }
        public string? PreviousBookingCode { get; set; }
        public DateTime? ExtractedAt { get; set; }
        public bool ManualParsingCompleted { get; set; }
        public string? ManualParsingNotes { get; set; }
        public DateTime? ManualParsingTimestamp { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Services.TourEmailsApiService
    public class ProcessedEmailDisplayTourEmailLibModel
    {
        public int Id { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string EmailType { get; set; } = string.Empty;
        public bool IsBooking { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string TourTime { get; set; } = string.Empty;
        public int NumberOfAdults { get; set; }
        public int NumberOfChildren { get; set; }
        public string CustomerEmail { get; set; } = string.Empty;
        public string CustomerPhone { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string TourLocation { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}

