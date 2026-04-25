using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Email.Models;

namespace Email.Services.State
{
    public sealed class TourDashboardCacheState
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private TourDashboardCacheSnapshot? _snapshot;

        public bool TryGetFreshSnapshot(TimeSpan ttl, [NotNullWhen(true)] out TourDashboardCacheSnapshot? snapshot)
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

        public void SaveSnapshot(TourDashboardCacheSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            snapshot.CachedAtUtc = DateTime.UtcNow;
            _snapshot = DeepClone(snapshot) ?? new TourDashboardCacheSnapshot();
        }

        public void SaveTreeState(TourTreeDisplayCacheState treeState)
        {
            ArgumentNullException.ThrowIfNull(treeState);

            _snapshot ??= new TourDashboardCacheSnapshot();
            _snapshot.TreeState = treeState.DeepClone();
            _snapshot.CachedAtUtc = DateTime.UtcNow;
        }

        public void Invalidate()
        {
            _snapshot = null;
        }

        private static TourDashboardCacheSnapshot? DeepClone(TourDashboardCacheSnapshot snapshot)
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            var clone = JsonSerializer.Deserialize<TourDashboardCacheSnapshot>(json, JsonOptions);
            if (clone is null)
            {
                return null;
            }

            clone.FilterArgs ??= new ShapedTreeFilterArgs();
            clone.TreeState ??= new TourTreeDisplayCacheState();
            return clone;
        }
    }
}
