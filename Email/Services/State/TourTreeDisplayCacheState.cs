using System;
using System.Collections.Generic;

namespace Email.Services.State
{
    public sealed class TourTreeDisplayCacheState
    {
        public HashSet<string> ExpandedNodePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int?> SelectedGuideIdByTour { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> SelectedMessageIdByTour { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public TourTreeDisplayCacheState DeepClone()
        {
            return new TourTreeDisplayCacheState
            {
                ExpandedNodePaths = new HashSet<string>(
                    ExpandedNodePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase),
                SelectedGuideIdByTour = new Dictionary<string, int?>(
                    SelectedGuideIdByTour ?? new Dictionary<string, int?>(),
                    StringComparer.OrdinalIgnoreCase),
                SelectedMessageIdByTour = new Dictionary<string, string?>(
                    SelectedMessageIdByTour ?? new Dictionary<string, string?>(),
                    StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
