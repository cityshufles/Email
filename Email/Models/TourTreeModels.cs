using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Email.Models
{
    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeNode
    public class TreeNode
    {
        public string Label { get; set; } = string.Empty;
        public List<TreeNode> Children { get; set; } = new List<TreeNode>();
        public bool IsExpanded { get; set; } = true;
        public string? Icon { get; set; }
        public string? Data { get; set; }
        public int? DailyGuestCount { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeBookingExtractionResult
    public class TreeBookingExtractionResult
    {
        public string MessageId { get; set; } = string.Empty;
        public string WalkerName { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int Attendees { get; set; }
        public int? NumberOfAdults { get; set; }
        public int? NumberOfChildren { get; set; }
        public string Language { get; set; } = string.Empty;
        public DateTime TourDate { get; set; }
        public string TourTime { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string EventUrl { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string? BookingStatus { get; set; }
        public bool IsCancellation { get; set; }
        public bool IsModification { get; set; }
        public bool IsConfirmation { get; set; }
        public string? EmailType { get; set; }
        public int? ProcessedEmailId { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeFilterRequest
    public class TreeFilterRequest
    {
        public DateTime? SelectedDate { get; set; }
        public string? VendorName { get; set; }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeData
    public class TreeData
    {
        public List<TreeNode> TreeNodes { get; set; } = new List<TreeNode>();
        public int TotalBookings { get; set; }
        public int TotalWalkers { get; set; }
        public Dictionary<string, int> VendorCounts { get; set; } = new Dictionary<string, int>();

        public string ToText()
        {
            var text = new StringBuilder();

            foreach (var dateNode in TreeNodes.OrderBy(n => n.Label))
            {
                text.AppendLine(dateNode.Label);

                foreach (var tourNode in dateNode.Children.OrderBy(n => n.Label))
                {
                    text.AppendLine($"  {tourNode.Label}");

                    foreach (var vendorNode in tourNode.Children.OrderBy(n => n.Label))
                    {
                        text.AppendLine($"    {vendorNode.Label}");

                        foreach (var walkerNode in vendorNode.Children.OrderBy(n => n.Label))
                        {
                            text.AppendLine($"      {walkerNode.Label}");
                        }
                    }
                }
            }

            return text.ToString();
        }
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeWalkerInfo
    public class TreeWalkerInfo
    {
        public string Name { get; set; } = string.Empty;
        public int Attendees { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public DateTime TourDate { get; set; }
        public string TourTime { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string EventUrl { get; set; } = string.Empty;
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeTourGroup
    public class TreeTourGroup
    {
        public string TourName { get; set; } = string.Empty;
        public DateTime TourDate { get; set; }
        public List<TreeVendorGroup> Vendors { get; set; } = new List<TreeVendorGroup>();
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeVendorGroup
    public class TreeVendorGroup
    {
        public string VendorName { get; set; } = string.Empty;
        public List<TreeWalkerInfo> Walkers { get; set; } = new List<TreeWalkerInfo>();
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.TreeStatistics
    public class TreeStatistics
    {
        public int TotalBookings { get; set; }
        public int TotalWalkers { get; set; }
        public int TotalTours { get; set; }
        public int TotalVendors { get; set; }
        public Dictionary<string, int> VendorCounts { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> TourCounts { get; set; } = new Dictionary<string, int>();
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.GuruWalkInMemoryRecord
    public class GuruWalkInMemoryRecord
    {
        public int Id { get; set; }
        public int Uid { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public DateTime ReceivedDate { get; set; }
        public string TextBody { get; set; } = string.Empty;
        public DateTime CollectedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string WalkerName { get; set; } = string.Empty;
        public string BookingCode { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public int Attendees { get; set; }
        public string Language { get; set; } = string.Empty;
        public DateTime? TourDate { get; set; }
        public string TourTime { get; set; } = string.Empty;
        public string TourName { get; set; } = string.Empty;
        public string EventUrl { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime ParsedAt { get; set; } = DateTime.UtcNow;
    }

    // Created: 2025-11-16 00:00 UTC - Copied from CityShufflesWorkStation.Models.GuruWalkInMemoryCache
    public class GuruWalkInMemoryCache
    {
        public List<GuruWalkInMemoryRecord> Records { get; set; } = new List<GuruWalkInMemoryRecord>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public bool IsInitialized { get; set; }
        public int TotalRecords { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
    }
}

