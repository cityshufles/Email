using System.Data;
using Microsoft.Data.SqlClient;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2025-12-09 - Service for retrieving message status with booking details
    /// </summary>
    public sealed class MessageStatusSqlService : IMessageStatusApiService
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public MessageStatusSqlService(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<MessageStatusDetail>> GetMessageStatusesAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT 
    bms.Id,
    bms.MessageId,
    bms.Stage,
    bms.SentFlag,
    bms.SentAtUtc,
    bms.Notes,
    b.CustomerName,
    b.TourName,
    b.TourDate,
    b.BookingCode,
    b.VendorName
FROM dbo.BookingMessageStatus bms
LEFT JOIN dbo.Bookings b ON 
    (bms.BookingId = b.Id) 
    OR 
    (bms.BookingId IS NULL AND bms.BookingCode = b.BookingCode AND bms.MessageId = b.MessageId)
ORDER BY bms.SentAtUtc DESC, bms.CreatedAt DESC;";

            var results = new List<MessageStatusDetail>();
            using var conn = _connectionFactory.CreateOpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(MapMessageStatusDetail(reader));
            }
            return results;
        }

        public async Task<List<GuideTourTreeNode>> GetGuideTourTreeAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
        {
            // Default to today if no start date provided
            var start = startDate ?? DateTime.Today;
            var end = endDate ?? DateTime.Today.AddDays(30);

            // Fetch flat data: Booking + Tour Info + Guide resolution
            const string sql = @"
SELECT 
    b.Id,
    b.CustomerName,
    b.CustomerPhone,
    b.NumberOfAttendees,
    b.TourName,
    b.TourDate,
    b.TourTime,
    b.TourLocation,
    t.Id AS TourId,
    t.DefaultGuideId,
    tgd.GuideId AS DefaultGuideIdOverride,
    g_default.FirstName AS DefaultGuideFirstName,
    g_default.LastName AS DefaultGuideLastName,
    g_override.FirstName AS OverrideGuideFirstName,
    g_override.LastName AS OverrideGuideLastName
FROM dbo.Bookings b
LEFT JOIN dbo.Tours t ON b.TourName = t.TourName
-- Join defaults to find time-specific guide
LEFT JOIN dbo.TourGuideDefaults tgd ON t.Id = tgd.TourId 
    AND tgd.IsActive = 1 
    AND tgd.DayOfWeek = DATEPART(dw, b.TourDate) 
    AND tgd.StartTime = b.TourTime
LEFT JOIN dbo.Guides g_default ON t.DefaultGuideId = g_default.Id
LEFT JOIN dbo.Guides g_override ON tgd.GuideId = g_override.Id
WHERE b.IsActive = 1 
  AND b.IsCancellation = 0 
  AND b.TourDate >= @StartDate 
  AND b.TourDate <= @EndDate
ORDER BY b.TourDate, b.TourTime, b.TourName;";

            var flatData = new List<dynamic>();
            using (var conn = _connectionFactory.CreateOpenConnection())
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.DateTime2) { Value = start });
                cmd.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.DateTime2) { Value = end });

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    flatData.Add(new
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        CustomerName = reader.IsDBNull(reader.GetOrdinal("CustomerName")) ? "" : reader.GetString(reader.GetOrdinal("CustomerName")),
                        CustomerPhone = reader.IsDBNull(reader.GetOrdinal("CustomerPhone")) ? "" : reader.GetString(reader.GetOrdinal("CustomerPhone")),
                        Attendees = reader.IsDBNull(reader.GetOrdinal("NumberOfAttendees")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("NumberOfAttendees")),
                        TourName = reader.IsDBNull(reader.GetOrdinal("TourName")) ? "" : reader.GetString(reader.GetOrdinal("TourName")),
                        TourDate = reader.IsDBNull(reader.GetOrdinal("TourDate")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("TourDate")),
                        TourTime = reader.IsDBNull(reader.GetOrdinal("TourTime")) ? "" : reader.GetString(reader.GetOrdinal("TourTime")),
                        DefaultGuideFirstName = reader.IsDBNull(reader.GetOrdinal("DefaultGuideFirstName")) ? "" : reader.GetString(reader.GetOrdinal("DefaultGuideFirstName")),
                        DefaultGuideLastName = reader.IsDBNull(reader.GetOrdinal("DefaultGuideLastName")) ? "" : reader.GetString(reader.GetOrdinal("DefaultGuideLastName")),
                        OverrideGuideFirstName = reader.IsDBNull(reader.GetOrdinal("OverrideGuideFirstName")) ? "" : reader.GetString(reader.GetOrdinal("OverrideGuideFirstName")),
                        OverrideGuideLastName = reader.IsDBNull(reader.GetOrdinal("OverrideGuideLastName")) ? "" : reader.GetString(reader.GetOrdinal("OverrideGuideLastName"))
                    });
                }
            }

            // Build Hierarchy: Date -> Guide -> Tour (Time) -> Walker
            var tree = new List<GuideTourTreeNode>();

            var byDate = flatData.GroupBy(x => x.TourDate?.Date ?? DateTime.MinValue).OrderBy(g => g.Key);

            foreach (var dateGroup in byDate)
            {
                var dateNode = new GuideTourTreeNode
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = dateGroup.Key.ToString("dddd M/d/yyyy"),
                    NodeType = "Date",
                    DateValue = dateGroup.Key,
                    SortString = dateGroup.Key.ToString("yyyyMMdd")
                };

                // Group by Resolved Guide
                var byGuide = dateGroup.GroupBy(x => ResolveGuideName((string)x.DefaultGuideFirstName, (string)x.DefaultGuideLastName, (string)x.OverrideGuideFirstName, (string)x.OverrideGuideLastName));

                foreach (var guideGroup in byGuide.OrderBy(g => g.Key))
                {
                    var guideNode = new GuideTourTreeNode
                    {
                        Id = Guid.NewGuid().ToString(),
                        ParentId = dateNode.Id,
                        Name = $"Guide: {guideGroup.Key}",
                        NodeType = "Guide",
                        SortString = guideGroup.Key
                    };
                    dateNode.Children.Add(guideNode);

                    // Group by Tour + Time
                    var byTour = guideGroup.GroupBy(x => new { Name = (string)x.TourName, Time = (string)x.TourTime });

                    foreach (var tourGroup in byTour.OrderBy(t => ParseTime(t.Key.Time)).ThenBy(t => t.Key.Name))
                    {
                        var tourNode = new GuideTourTreeNode
                        {
                            Id = Guid.NewGuid().ToString(),
                            ParentId = guideNode.Id,
                            Name = $"Tour: {tourGroup.Key.Name}",
                            Time = tourGroup.Key.Time,
                            NodeType = "Tour",
                            SortString = $"{ParseTime(tourGroup.Key.Time):HHmm} {tourGroup.Key.Name}"
                        };
                        guideNode.Children.Add(tourNode);

                        foreach (var walker in tourGroup)
                        {
                            var walkerNode = new GuideTourTreeNode
                            {
                                Id = Guid.NewGuid().ToString(),
                                ParentId = tourNode.Id,
                                Name = walker.CustomerName,
                                Attendees = walker.Attendees,
                                Phone = walker.CustomerPhone,
                                NodeType = "Walker",
                                SortString = walker.CustomerName
                            };
                            tourNode.Children.Add(walkerNode);
                        }
                    }
                }
                tree.Add(dateNode);
            }

            return tree;
        }

        private static string ResolveGuideName(string defFirst, string defLast, string ovrFirst, string ovrLast)
        {
            if (!string.IsNullOrWhiteSpace(ovrFirst)) return $"{ovrFirst} {ovrLast}".Trim();
            if (!string.IsNullOrWhiteSpace(defFirst)) return $"{defFirst} {defLast}".Trim();
            return "Unassigned";
        }

        private static DateTime ParseTime(string timeStr)
        {
            if (DateTime.TryParse(timeStr, out var dt)) return dt;
            return DateTime.MinValue;
        }

        private static MessageStatusDetail MapMessageStatusDetail(SqlDataReader r)
        {
            return new MessageStatusDetail
            {
                Id = r.GetInt32(r.GetOrdinal("Id")),
                MessageId = r.GetString(r.GetOrdinal("MessageId")),
                Stage = r.GetString(r.GetOrdinal("Stage")),
                SentFlag = r.GetBoolean(r.GetOrdinal("SentFlag")),
                SentAtUtc = r.IsDBNull(r.GetOrdinal("SentAtUtc")) ? null : r.GetDateTime(r.GetOrdinal("SentAtUtc")),
                Notes = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
                CustomerName = r.IsDBNull(r.GetOrdinal("CustomerName")) ? null : r.GetString(r.GetOrdinal("CustomerName")),
                TourName = r.IsDBNull(r.GetOrdinal("TourName")) ? null : r.GetString(r.GetOrdinal("TourName")),
                TourDate = r.IsDBNull(r.GetOrdinal("TourDate")) ? null : r.GetDateTime(r.GetOrdinal("TourDate")),
                BookingCode = r.IsDBNull(r.GetOrdinal("BookingCode")) ? null : r.GetString(r.GetOrdinal("BookingCode")),
                VendorName = r.IsDBNull(r.GetOrdinal("VendorName")) ? null : r.GetString(r.GetOrdinal("VendorName"))
            };
        }
    }
}

