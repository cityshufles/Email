using System;
using System.Collections.Generic;

namespace Email.Models.Reports
{
    public sealed class BookingsLiveListItem
    {
        public int BookingId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerIdentifier { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsConfirmation { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime ActivityAtUtc { get; set; }
        public bool NeedsNumber { get; set; }
        public int? NumberOfAttendees { get; set; }
        public int? NumberOfAdults { get; set; }
        public int? NumberOfChildren { get; set; }
        public string? Language { get; set; }
        public string? BookingStatus { get; set; }
    }

    public sealed class CustomerCommunicationProfile
    {
        public BookingsLiveListItem? SelectedBooking { get; set; }
        public List<BookingTimelineEvent> BookingTimeline { get; set; } = new();
        public List<CommunicationEvent> CommunicationEvents { get; set; } = new();
        public GuestContactLabels Labels { get; set; } = new();
        public WalkerGuideReportSummary GuideReport { get; set; } = new();
        public GalleryResolutionInfo Gallery { get; set; } = new();
        public List<string> SentStages { get; set; } = new();
        public List<MessageStageSnapshot> SentStageSnapshots { get; set; } = new();
    }

    public sealed class MessageStageSnapshot
    {
        public string Stage { get; set; } = string.Empty;
        public DateTime? SentAtUtc { get; set; }
    }

    public sealed class BookingTimelineEvent
    {
        public int BookingId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerIdentifier { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public bool IsActive { get; set; }
        public DateTime OccurredAtUtc { get; set; }
    }

    public sealed class CommunicationEvent
    {
        public int Id { get; set; }
        public int? CustomerId { get; set; }
        public string? CustomerIdentifier { get; set; }
        public int? BookingId { get; set; }
        public string? BookingCode { get; set; }
        public string? MessageId { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        public string? VendorName { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public string? TemplateType { get; set; }
        public string? TemplateName { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public string? TriggeredBy { get; set; }
        public DateTime SentAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class GuestContactLabels
    {
        public bool NeedsNumber { get; set; }
        public bool ContactedOnPlatform { get; set; }
        public bool WhatsAppWorks { get; set; }
        public bool SmsWorks { get; set; }

        public bool GetByKey(string key)
        {
            return NormalizeKey(key) switch
            {
                "needs_number" => NeedsNumber,
                "contacted_on_platform" => ContactedOnPlatform,
                "whatsapp_works" => WhatsAppWorks,
                "sms_works" => SmsWorks,
                _ => false
            };
        }

        public void SetByKey(string key, bool value)
        {
            switch (NormalizeKey(key))
            {
                case "needs_number":
                    NeedsNumber = value;
                    break;
                case "contacted_on_platform":
                    ContactedOnPlatform = value;
                    break;
                case "whatsapp_works":
                    WhatsAppWorks = value;
                    break;
                case "sms_works":
                    SmsWorks = value;
                    break;
            }
        }

        public static string NormalizeKey(string? key)
        {
            return (key ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "needs number" => "needs_number",
                "needsnumber" => "needs_number",
                "needs_number" => "needs_number",
                "contacted on platform" => "contacted_on_platform",
                "contacted_on_platform" => "contacted_on_platform",
                "contactedonplatform" => "contacted_on_platform",
                "whatsapp works" => "whatsapp_works",
                "whatsappworks" => "whatsapp_works",
                "whatsapp_works" => "whatsapp_works",
                "sms works" => "sms_works",
                "smsworks" => "sms_works",
                "sms_works" => "sms_works",
                _ => (key ?? string.Empty).Trim().ToLowerInvariant()
            };
        }
    }

    public sealed class BookingMessageEventWriteModel
    {
        public int? CustomerId { get; set; }
        public string? CustomerIdentifier { get; set; }
        public int? BookingId { get; set; }
        public string? BookingCode { get; set; }
        public string? MessageId { get; set; }
        public string? TourName { get; set; }
        public DateTime? TourDate { get; set; }
        public string? TourTime { get; set; }
        public string? VendorName { get; set; }
        public string Stage { get; set; } = "misc";
        public string Channel { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public string? TemplateType { get; set; }
        public string? TemplateName { get; set; }
        public string TriggerType { get; set; } = "open_channel";
        public string? TriggeredBy { get; set; }
        public DateTime? SentAtUtc { get; set; }
    }

    public sealed class WalkerGuideReportSummary
    {
        public bool ReportFound { get; set; }
        public bool IsSubmitted { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public bool? IsCheckedIn { get; set; }
        public string? ReviewStatus { get; set; }
        public string? ReviewNotes { get; set; }
        public bool? DoNotContact { get; set; }
        public int? ActualAdults { get; set; }
        public int? ActualChildren { get; set; }
        public int? ActualAttendees { get; set; }
        public string? PublicId { get; set; }
    }

    public sealed class GalleryResolutionInfo
    {
        public bool Found { get; set; }
        public string? Url { get; set; }
        public string? PublicId { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
