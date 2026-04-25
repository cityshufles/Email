using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Email.Services.State
{
    public sealed class MobileTourDashboardCacheState
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private MobileTourDashboardCacheSnapshot? _snapshot;

        public bool TryGetFreshSnapshot(TimeSpan ttl, [NotNullWhen(true)] out MobileTourDashboardCacheSnapshot? snapshot)
        {
            snapshot = null;

            var current = _snapshot;
            if (current is null)
            {
                return false;
            }

            if (ttl > TimeSpan.Zero && DateTime.UtcNow - current.CachedAtUtc > ttl)
            {
                return false;
            }

            snapshot = DeepClone(current);
            return snapshot is not null;
        }

        public void SaveSnapshot(MobileTourDashboardCacheSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            snapshot.CachedAtUtc = DateTime.UtcNow;
            _snapshot = DeepClone(snapshot) ?? new MobileTourDashboardCacheSnapshot();
        }

        public void Invalidate()
        {
            _snapshot = null;
        }

        private static MobileTourDashboardCacheSnapshot? DeepClone(MobileTourDashboardCacheSnapshot snapshot)
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var clone = JsonSerializer.Deserialize<MobileTourDashboardCacheSnapshot>(json, JsonOptions);
            if (clone is null)
            {
                return null;
            }

            clone.MobileTours ??= new();
            clone.SelectedGuideIdByTour = CreateGuideDictionary(clone.SelectedGuideIdByTour);
            clone.SelectedMessageIdByTour = CreateMessageDictionary(clone.SelectedMessageIdByTour);
            return clone;
        }

        private static Dictionary<string, int?> CreateGuideDictionary(Dictionary<string, int?>? source)
        {
            return source == null
                ? new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int?>(source, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string> CreateMessageDictionary(Dictionary<string, string>? source)
        {
            return source == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
        }
    }
}
