namespace Email.Controllers.GuideLite;

public sealed class GuideLiteTourDto
{
    public string TourDate { get; set; } = string.Empty;
    public string TourName { get; set; } = string.Empty;
    public string TourTime { get; set; } = string.Empty;
    public string DisplayTime { get; set; } = string.Empty;
    public int TotalGuests { get; set; }
    public int? GuideId { get; set; }
    public bool IsSubmitted { get; set; }
    public string GroupFamilyKey { get; set; } = string.Empty;
    public string GroupTimeKey { get; set; } = string.Empty;
    public string ListDisplayName { get; set; } = string.Empty;
}

public sealed class GuideLiteGuideOptionDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class GuideLiteToursResponseDto
{
    public string RangeStart { get; set; } = string.Empty;
    public string RangeEnd { get; set; } = string.Empty;
    public string GuideFilter { get; set; } = "all";
    public List<GuideLiteGuideOptionDto> Guides { get; set; } = new();
    public List<GuideLiteTourDto> Tours { get; set; } = new();
}

public sealed class GuideLiteReportDto
{
    public int Id { get; set; }
    public string TourDate { get; set; } = string.Empty;
    public string TourName { get; set; } = string.Empty;
    public string TourTime { get; set; } = string.Empty;
    public string DisplayTime { get; set; } = string.Empty;
    public int? GuideId { get; set; }
    public string GuideName { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public string? GeneralNotes { get; set; }
    public string? ImagePaths { get; set; }
    public bool IsSubmitted { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public bool CanShareLinks { get; set; }
    public string GalleryUrl { get; set; } = string.Empty;
    public Dictionary<string, string> VendorGalleryLinks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Photos { get; set; } = new();
    public List<GuideLiteWalkerDto> Walkers { get; set; } = new();
}

public sealed class GuideLiteWalkerDto
{
    public int BookingId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? VendorName { get; set; }
    public int BookedAdults { get; set; }
    public int BookedChildren { get; set; }
    public int ActualAdults { get; set; }
    public int ActualChildren { get; set; }
    public int ActualAttendees { get; set; }
    public bool IsCheckedIn { get; set; }
    public bool DoNotContact { get; set; }
    public string? ReviewStatus { get; set; }
    public string? ReviewNotes { get; set; }
    public bool IsWalkUp { get; set; }
    public string GalleryUrl { get; set; } = string.Empty;
    public string WhatsAppLink { get; set; } = string.Empty;
    public string SmsLink { get; set; } = string.Empty;
}

public sealed class GuideLiteAttendeesRequest
{
    public int ActualAdults { get; set; }
    public int ActualChildren { get; set; }
}

public sealed class GuideLiteBookingStateRequest
{
    public string? ReviewStatus { get; set; }
    public string? ReviewNotes { get; set; }
    public bool IsCheckedIn { get; set; }
    public bool DoNotContact { get; set; }
}

public sealed class GuideLitePhoneRequest
{
    public string Phone { get; set; } = string.Empty;
}

public sealed class GuideLiteWalkUpCreateRequest
{
    public string TourDate { get; set; } = string.Empty;
    public string TourName { get; set; } = string.Empty;
    public string? TourTime { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int ActualAdults { get; set; } = 1;
    public int ActualChildren { get; set; }
    public string? ReviewNotes { get; set; }
}

public sealed class GuideLiteWalkUpUpdateRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public int ActualAdults { get; set; } = 1;
    public int ActualChildren { get; set; }
    public string? ReviewNotes { get; set; }
}

public sealed class GuideLitePhotoSyncRequest
{
    public string TourDate { get; set; } = string.Empty;
    public string TourName { get; set; } = string.Empty;
    public string? TourTime { get; set; }
}

public sealed class GuideLiteSubmitReportRequest
{
    public string TourDate { get; set; } = string.Empty;
    public string TourName { get; set; } = string.Empty;
    public string? TourTime { get; set; }
    public int? GuideId { get; set; }
    public string? PublicId { get; set; }
    public string? GeneralNotes { get; set; }
    public string? ImagePaths { get; set; }
    public List<GuideLiteSubmitWalkerRequest> Walkers { get; set; } = new();
}

public sealed class GuideLiteSubmitWalkerRequest
{
    public int BookingId { get; set; }
    public int ActualAdults { get; set; }
    public int ActualChildren { get; set; }
    public bool IsCheckedIn { get; set; }
    public bool DoNotContact { get; set; }
    public string? ReviewStatus { get; set; }
    public string? ReviewNotes { get; set; }
}
