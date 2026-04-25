using System;

namespace Email.TourTreeViewShapedData.Enums
{
    /// <summary>
    /// Created: 2025-11-16 00:00 UTC - Copied from TourEmails.TourTreeViewShapedData.Enums.TreeDataFilterType
    /// Filter options for retrieving shaped tour tree data.
    /// </summary>
    public enum TreeDataFilterType
    {
        ConfirmationsOnly = 0,
        CancellationsOnly = 1,
        ModificationsOnly = 2,
        ActiveBookings = 3,
        TodayOnly = 4,
        All = 5,
        AllUnfiltered = 6
    }
}

