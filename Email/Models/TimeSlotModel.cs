namespace Email.Models
{
    public class TimeSlotModel : ITourLogistics
    {
        public string TourTime { get; set; } = string.Empty;
        
        // ITourLogistics Implementation
        public string? MeetingTime { get; set; }
        public string? MeetingPlace { get; set; }
        public string? MeetingInstructions { get; set; }
        public string? VendorLink { get; set; }
    }
}
