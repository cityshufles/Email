using System;
using Microsoft.AspNetCore.Components;
using Email.Models;
using Email.Services.State;
using Email.TourTreeViewShapedData.Enums;

namespace Email.Pages
{
    public partial class TourManagementDashboard
    {
        private static readonly TimeSpan DashboardCacheTtl = TimeSpan.FromMinutes(5);

        [Inject] protected TourDashboardCacheState DashboardCacheState { get; set; } = default!;

        protected Task RefreshTreeDataForceFreshAsync() => RefreshTreeDataAsync(forceFresh: true);

        private bool ShouldAttemptInitializationRestore(bool forceFresh)
        {
            return !forceFresh && tourTreeData == null && filteredTourTreeData == null;
        }

        private bool TryRestoreDashboardFromCache()
        {
            if (!DashboardCacheState.TryGetFreshSnapshot(DashboardCacheTtl, out var snapshot) || snapshot == null)
            {
                return false;
            }

            activeTab = string.IsNullOrWhiteSpace(snapshot.ActiveTab) ? "tours" : snapshot.ActiveTab;
            filterArgs = CloneFilterArgs(snapshot.FilterArgs);

            if (snapshot.TreeData == null)
            {
                tourTreeData = null;
                filteredTourTreeData = null;
                return false;
            }

            tourTreeData = snapshot.TreeData;
            filteredTourTreeData = snapshot.TreeData;
            return true;
        }

        private void SaveDashboardSnapshotToCache()
        {
            var snapshot = new TourDashboardCacheSnapshot
            {
                ActiveTab = string.IsNullOrWhiteSpace(activeTab) ? "tours" : activeTab,
                FilterArgs = CloneFilterArgs(filterArgs),
                TreeData = filteredTourTreeData ?? tourTreeData
            };

            if (DashboardCacheState.TryGetFreshSnapshot(TimeSpan.MaxValue, out var existing) && existing != null)
            {
                snapshot.TreeState = existing.TreeState?.DeepClone() ?? new TourTreeDisplayCacheState();
            }

            DashboardCacheState.SaveSnapshot(snapshot);
        }

        private static ShapedTreeFilterArgs CloneFilterArgs(ShapedTreeFilterArgs? source)
        {
            if (source == null)
            {
                return new ShapedTreeFilterArgs
                {
                    FilterType = TreeDataFilterType.ActiveBookings,
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(2)
                };
            }

            return new ShapedTreeFilterArgs
            {
                FilterType = source.FilterType,
                StartDate = source.StartDate,
                EndDate = source.EndDate,
                Vendor = source.Vendor
            };
        }
    }
}
