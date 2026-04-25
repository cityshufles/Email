using System;

namespace Email.Services.GmailCollection.Dtos
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// Request parameters for v2 Gmail collection.
    /// </summary>
    public sealed class CollectionRequestDto
    {
        public string Window { get; set; } = "day"; // day|week|month|daysPrior
        public int? DaysPrior { get; set; } // used when Window == daysPrior
        public int? Limit { get; set; } // cap the total collected emails
        public int BatchSize { get; set; } = 250; // per-batch fetch size
        public bool IgnoreWatermark { get; set; } = false; // when true, bypass LastSeenUid filter
    }
}


