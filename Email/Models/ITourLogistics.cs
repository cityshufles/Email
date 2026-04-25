namespace Email.Models
{
    public interface ITourLogistics
    {
        string? MeetingTime { get; set; }
        string? MeetingPlace { get; set; }
        string? MeetingInstructions { get; set; }
        string? VendorLink { get; set; }
    }
}
