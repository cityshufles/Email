using System;

namespace Email.Calendar.Models
{
    public class GoogleAppointmentModel
    {
        public string Id { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool IsAllDay { get; set; }
        public string? Etag { get; set; }
        public string? ColorId { get; set; }
        
        // Additional properties usually needed for Syncfusion mapping
        public string Subject 
        { 
            get => Summary; 
            set => Summary = value; 
        }
    }
}
