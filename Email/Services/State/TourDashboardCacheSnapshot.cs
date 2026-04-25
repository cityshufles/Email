using System;
using Email.Models;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Services.State
{
    public sealed class TourDashboardCacheSnapshot
    {
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
        public string ActiveTab { get; set; } = "tours";
        public ShapedTreeFilterArgs FilterArgs { get; set; } = CreateDefaultFilterArgs();
        public ShapedTreeData? TreeData { get; set; }
        public TourTreeDisplayCacheState TreeState { get; set; } = new();

        private static ShapedTreeFilterArgs CreateDefaultFilterArgs()
        {
            return new ShapedTreeFilterArgs
            {
                FilterType = TreeDataFilterType.ActiveBookings,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2)
            };
        }
    }
}
