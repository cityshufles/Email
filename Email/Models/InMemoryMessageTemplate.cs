namespace Email.Models
{
    public class InMemoryMessageTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Content { get; set; } = "";
        public string Description { get; set; } = "";

        // Optional metadata mapped from DbTourMessage to support replacements in the tree
        public string? TourName { get; set; }
        public string? TourStartTime { get; set; }
        public int? GuideId { get; set; }
        public string? MeetingPlace { get; set; }

        public string? MeetingLocation { get; set; }
        public string? MeetingTime { get; set; }
        public string? MeetingInstructions { get; set; }
        public string? Signature { get; set; }
        public string? VendorLink { get; set; }
        public bool? IsActive { get; set; }
    }
}


