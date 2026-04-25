namespace Email.Models
{
    /// <summary>
    /// Represents an available tour time option with optional logistics and override labeling.
    /// </summary>
    public class MasterTourTimeSlot
    {
        public string TourTime { get; set; } = string.Empty;
        public string? MeetingTime { get; set; }
        public string? MeetingPlace { get; set; }
        public string? MeetingInstructions { get; set; }
        public bool IsOverride { get; set; }
    }
}
