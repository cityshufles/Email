using System;
using System.Collections.Generic;
using Email.Models.Mobile;

namespace Email.Services.State
{
    public sealed class MobileTourDashboardCacheSnapshot
    {
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? SelectedDate { get; set; } = DateTime.Today;
        public List<MobileTourCardModel> MobileTours { get; set; } = new();
        public Dictionary<string, int?> SelectedGuideIdByTour { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SelectedMessageIdByTour { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
