using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Email.Models;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;
using Email.Services;


namespace Email.TourTreeViewShapedData
{
    /// <summary>
    /// Consolidated class for querying, shaping, and returning tree data
   /// Created: 2025-09-13
    /// Updated: 2025-10-03 12:25 PM EDT - Enhanced confirmation filtering to respect Bookings table state
    /// Modified: 2026-02-11 - Added ITourNameNormalizer for DB-driven master tour name display
    /// </summary>
    public class TourTreeDataProviderSqlServer
    {
        private readonly string _connectionString;
        private readonly ITourNameNormalizer _tourNameNormalizer;
        // 2025-12-02 00:00 UTC - Track current filter to adjust status rendering (mods counted as confirmed in confirmations view)
        private TreeDataFilterType _currentFilterType;

        public TourTreeDataProviderSqlServer(string connectionString, ITourNameNormalizer tourNameNormalizer)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tourNameNormalizer = tourNameNormalizer ?? throw new ArgumentNullException(nameof(tourNameNormalizer));
        }

        public async Task<ShapedTreeData> GetShapedTreeDataAsync(
            TreeDataFilterType filterType = TreeDataFilterType.ConfirmationsOnly,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? vendorFilter = null)
        {
            try
            {
                // 2025-11-17: Console trace for shaped data provider entry
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] GetShapedTreeDataAsync start: FilterType={filterType}, Start={startDate:yyyy-MM-dd}, End={endDate:yyyy-MM-dd}, Vendor={vendorFilter ?? "(null)"}"); } catch { }

                // Remember the active filter for downstream rendering decisions
                _currentFilterType = filterType;

                var processedEmails = GetProcessedEmails(filterType, startDate, endDate, vendorFilter);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] ProcessedEmails loaded: {processedEmails.Count}"); } catch { }

                var messageIds = processedEmails
                    .Select(p => p.MessageId)
                    .Where(mid => !string.IsNullOrWhiteSpace(mid))
                    .Distinct()
                    .ToList();

                var adultsChildren = LoadAdultsChildrenByMessageId(messageIds);
                var bookingStatus = LoadBookingStatusByMessageId(messageIds);
                var messageSent = LoadMessageSentByMessageId(messageIds);
                var messageStages = LoadMessageStagesByMessageId(messageIds);
                var customerIdentity = LoadCustomerIdentityByMessageId(messageIds);
                var checkInByMessageId = LoadCheckInByMessageId(messageIds);
                var contactStatesByBooking = LoadContactChannelStatesByBooking(processedEmails);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] Enrichment sets - MsgIds={messageIds.Count}, AdultsChildren={adultsChildren.Count}, BookingStatus={bookingStatus.Count}, MessageSent={messageSent.Count}, MessageStages={messageStages.Count}, Identity={customerIdentity.Count}, CheckIns={checkInByMessageId.Count}, ContactStates={contactStatesByBooking.Count}"); } catch { }

                var walkers = TransformToShapedWalkers(processedEmails, adultsChildren, bookingStatus, messageSent, messageStages, customerIdentity, checkInByMessageId, contactStatesByBooking);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] Walkers shaped: {walkers.Count}"); } catch { }

                // 2026-02-11 - Pre-load tour name -> master name mappings for efficient tree building
                var tourNameMappings = await BuildTourNameMappingCacheAsync(walkers);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] Built tour name mapping cache: {tourNameMappings.Count} entries"); } catch { }

                // 2026-02-27 - Apply schedule overrides by canonical TourId when available.
                var schedules = LoadTourSchedules(startDate, endDate);
                ApplyScheduleOverrides(walkers, schedules, tourNameMappings);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] Applied schedule overrides. Schedules found: {schedules.Count}"); } catch { }

                // 2026-01-31 - Load assignments
                var assignments = LoadTourGuideAssignments(startDate, endDate);
                
                // 2026-02-03 - Derive assignments from active schedules
                var scheduleAssignments = DeriveAssignmentsFromSchedules(schedules, startDate, endDate);
                assignments.AddRange(scheduleAssignments);

                var reportGroupStatusKeys = BuildReportGroupStatusKeys(startDate, endDate, tourNameMappings);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] Report groups loaded: Submitted={reportGroupStatusKeys.SubmittedKeys.Count}, Unsubmitted={reportGroupStatusKeys.UnsubmittedKeys.Count}"); } catch { }

                var shaped = BuildShapedTreeStructure(walkers, assignments, tourNameMappings, reportGroupStatusKeys);
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] Shaped tree built: DateNodes={shaped.TreeNodes.Count}"); } catch { }
                return shaped;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] ERROR: {ex.Message}"); } catch { }
                throw;
            }
        }


        /// <summary>
        /// Retrieves processed emails from the database based on the specified filters.
        /// </summary>
        /// <remarks>
        /// Updated: 2026-03-30
        /// Change: ActiveBookings now includes explicit confirmation email types and excludes modifications.
        /// </remarks>
        /// <param name="filterType">Requested tree filter type.</param>
        /// <param name="startDate">Inclusive tour-date lower bound when provided.</param>
        /// <param name="endDate">Exclusive tour-date upper bound when provided.</param>
        /// <param name="vendorFilter">Optional vendor filter.</param>
        /// <returns>Filtered processed email records used to build the shaped tree.</returns>
        private List<ProcessedEmail> GetProcessedEmails(
            TreeDataFilterType filterType,
            DateTime? startDate,
            DateTime? endDate,
            string? vendorFilter)
        {
            var emails = new List<ProcessedEmail>();
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var sql = @"SELECT Id, InboxEmailId, MessageId, VendorName, EmailType, IsTourBookingEmail,
                                   ClassificationRuleId, ProcessingStatus, ProcessingStartedAt, ProcessingCompletedAt,
                                   ProcessingError, ProcessingAttempts, NextProcessingAttempt, RateLimitResetAt,
                                   CustomerName, BookingCode, CustomerPhone, CustomerEmail, NumberOfAttendees,
                                   NumberOfAdults, NumberOfChildren, Language, TourDate, TourTime, TourName,
                                   TourLocation, ExtractedAt, CustomerIdentifier, IsLatestAction, CreatedAt, UpdatedAt,
                                   ManualParsingCompleted, ManualParsingNotes, ManualParsingTimestamp, PlainTextContent,
                                   IsCancellation, IsModification, IsBooking, AssociatedBookingIds, BookingAlterationNotes,
                                   HtmlContent, ExtractedBookingCode, NewBookingCode, PreviousBookingCode,
                                   (SELECT TOP 1 b.TourDayOfWeek FROM Bookings b WHERE b.BookingCode = AutomaticGmail_ProcessedEmails.BookingCode) AS TourDayOfWeek,
                                   (SELECT TOP 1 b.DisplayDate FROM Bookings b WHERE b.BookingCode = AutomaticGmail_ProcessedEmails.BookingCode) AS DisplayDate,
                                   (SELECT TOP 1 b.DisplayTime FROM Bookings b WHERE b.BookingCode = AutomaticGmail_ProcessedEmails.BookingCode) AS DisplayTime
                            FROM AutomaticGmail_ProcessedEmails WHERE 1=1";

                var parameters = new List<SqlParameter>();

                switch (filterType)
                {
                    case TreeDataFilterType.ConfirmationsOnly:
                        // Include booking/confirmation and also modifications that represent current active bookings
                        sql += " AND (EmailType = 'booking' OR EmailType = 'Booking Confirmation' OR EmailType = 'confirmation' OR EmailType LIKE '%booking%' OR EmailType = 'modification' OR IsModification = 1)";
                        // Exclude cancellations even if later status flipped to cancelled
                        sql += " AND (IsCancellation = 0 AND (ProcessingStatus IS NULL OR ProcessingStatus != 'cancelled') AND EmailType != 'cancellation')";
                        // Updated: 2025-10-03 12:25 PM EDT - Use Bookings.BookingCode as authoritative state source
                        sql += " AND NOT EXISTS (SELECT 1 FROM Bookings b WHERE b.BookingCode = AutomaticGmail_ProcessedEmails.BookingCode AND (b.IsCancellation = 1 OR b.IsActive = 0 OR UPPER(b.BookingStatus) = 'CANCELLED'))";
                        break;
                    case TreeDataFilterType.CancellationsOnly:
                        sql += " AND (EmailType = 'cancellation' OR IsCancellation = 1 OR ProcessingStatus = 'cancelled')";
                        break;
                    case TreeDataFilterType.ModificationsOnly:
                        sql += " AND (EmailType = 'modification' OR IsModification = 1)";
                        break;
                    case TreeDataFilterType.ActiveBookings:
                        // Updated: 2026-03-30 - Include explicit confirmations while excluding modifications.
                        sql += " AND (EmailType = 'booking' OR EmailType = 'Booking Confirmation' OR EmailType = 'confirmation' OR EmailType LIKE '%booking%')";
                        sql += " AND (ISNULL(IsModification, 0) = 0 AND (EmailType IS NULL OR LOWER(EmailType) != 'modification'))";
                        sql += " AND (IsCancellation = 0 AND (ProcessingStatus IS NULL OR ProcessingStatus != 'cancelled') AND EmailType != 'cancellation')";
                        // Updated: 2025-10-03 12:25 PM EDT - Mirror confirmation filter against Bookings state
                        sql += " AND NOT EXISTS (SELECT 1 FROM Bookings b WHERE b.BookingCode = AutomaticGmail_ProcessedEmails.BookingCode AND (b.IsCancellation = 1 OR b.IsActive = 0 OR UPPER(b.BookingStatus) = 'CANCELLED'))";
                        break;
                    case TreeDataFilterType.TodayOnly:
                        // Use range [StartDate, EndDate) if provided; otherwise default to today..tomorrow
                        if (!startDate.HasValue)
                        {
                            startDate = DateTime.Today;
                        }
                        if (!endDate.HasValue)
                        {
                            endDate = startDate.Value.Date.AddDays(1);
                        }
                        break;
                    case TreeDataFilterType.All:
                        // No extra filter; include all records matching optional date/vendor filters
                        break;
                }

                if (filterType != TreeDataFilterType.AllUnfiltered && startDate.HasValue)
                {
                    sql += " AND TourDate >= @startDate";
                    parameters.Add(new SqlParameter("@startDate", startDate.Value.ToString("yyyy-MM-dd")));
                }
                if (filterType != TreeDataFilterType.AllUnfiltered && endDate.HasValue)
                {
                    sql += " AND TourDate < @endDate";
                    parameters.Add(new SqlParameter("@endDate", endDate.Value.ToString("yyyy-MM-dd")));
                }
                if (!string.IsNullOrWhiteSpace(vendorFilter))
                {
                    sql += " AND VendorName = @vendor";
                    parameters.Add(new SqlParameter("@vendor", vendorFilter));
                }

                sql += " ORDER BY TourDate, TourName, CustomerName";

                using var command = new SqlCommand(sql, connection);
                foreach (var p in parameters) command.Parameters.Add(p);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    emails.Add(MapReaderToProcessedEmail(reader));
                }

                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] GetProcessedEmails: {emails.Count} rows"); } catch { }
                return emails;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] GetProcessedEmails ERROR: {ex.Message}"); } catch { }
                throw;
            }
        }

        private Dictionary<string, (int? adults, int? children)> LoadAdultsChildrenByMessageId(List<string> messageIds)
        {
            var result = new Dictionary<string, (int?, int?)>();
            if (messageIds == null || messageIds.Count == 0) return result;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var paramNames = new List<string>();
                using var command = connection.CreateCommand();
                for (int i = 0; i < messageIds.Count; i++)
                {
                    var name = $"@m{i}";
                    paramNames.Add(name);
                    command.Parameters.AddWithValue(name, messageIds[i]);
                }

                command.CommandText = $@"SELECT MessageId, NumberOfAdults, NumberOfChildren FROM Bookings
                                          WHERE MessageId IN ({string.Join(",", paramNames)})";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var msgId = reader.GetString(0);
                    var adults = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
                    var children = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                    result[msgId] = (adults, children);
                }

                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadAdultsChildren: {result.Count} msgIds"); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadAdultsChildren WARN: {ex.Message}"); } catch { }
                return new Dictionary<string, (int?, int?)>();
            }
        }

        private Dictionary<string, (string? bookingStatus, bool isCancellation, bool isModification, bool isConfirmation, string? emailType)> LoadBookingStatusByMessageId(List<string> messageIds)
        {
            var result = new Dictionary<string, (string?, bool, bool, bool, string?)>();
            if (messageIds == null || messageIds.Count == 0) return result;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var paramNames = new List<string>();
                using var command = connection.CreateCommand();
                for (int i = 0; i < messageIds.Count; i++)
                {
                    var name = $"@m{i}";
                    paramNames.Add(name);
                    command.Parameters.AddWithValue(name, messageIds[i]);
                }

                command.CommandText = $@"SELECT MessageId, BookingStatus, IsCancellation, IsModification, IsConfirmation, EmailType
                                          FROM Bookings WHERE MessageId IN ({string.Join(",", paramNames)})";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var msgId = reader.GetString(0);
                    var bookingStatus = reader.IsDBNull(1) ? null : reader.GetString(1);
                    var isCancellation = reader.GetBoolean(2);
                    var isModification = reader.GetBoolean(3);
                    var isConfirmation = reader.GetBoolean(4);
                    var emailType = reader.IsDBNull(5) ? null : reader.GetString(5);
                    result[msgId] = (bookingStatus, isCancellation, isModification, isConfirmation, emailType);
                }

                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadBookingStatus: {result.Count} msgIds"); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadBookingStatus WARN: {ex.Message}"); } catch { }
                return new Dictionary<string, (string?, bool, bool, bool, string?)>();
            }
        }

        // 2025-12-07 00:00 UTC - Load message sent flag/timestamp for walker badge
        private Dictionary<string, (bool sent, DateTime? sentAtUtc)> LoadMessageSentByMessageId(List<string> messageIds)
        {
            var result = new Dictionary<string, (bool, DateTime?)>();
            if (messageIds == null || messageIds.Count == 0) return result;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var paramNames = new List<string>();
                using var command = connection.CreateCommand();
                for (int i = 0; i < messageIds.Count; i++)
                {
                    var name = $"@m{i}";
                    paramNames.Add(name);
                    command.Parameters.AddWithValue(name, messageIds[i]);
                }

                command.CommandText = $@"SELECT MessageId, MessageSent, MessageSentAtUtc 
                                          FROM Bookings 
                                          WHERE MessageId IN ({string.Join(",", paramNames)})";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var msgId = reader.GetString(0);
                    var sent = reader.IsDBNull(1) ? false : reader.GetBoolean(1);
                    var sentAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);

                    if (result.TryGetValue(msgId, out var existing))
                    {
                        var (_, existingSentAtUtc) = existing;
                        var existingDate = existingSentAtUtc ?? DateTime.MinValue;
                        var incomingDate = sentAt ?? DateTime.MinValue;
                        if (incomingDate > existingDate)
                        {
                            result[msgId] = (sent, sentAt);
                        }
                    }
                    else
                    {
                        result[msgId] = (sent, sentAt);
                    }
                }

                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadMessageSent: {result.Count} msgIds"); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadMessageSent WARN: {ex.Message}"); } catch { }
                return new Dictionary<string, (bool, DateTime?)>();
            }
        }

        // 2026-03-13 - Load booking-level contact channel states (sms/wa/platform) keyed by MessageId + BookingCode.
        private Dictionary<string, (byte smsState, byte waState, byte platformState)> LoadContactChannelStatesByBooking(List<ProcessedEmail> processedEmails)
        {
            var result = new Dictionary<string, (byte smsState, byte waState, byte platformState)>(StringComparer.OrdinalIgnoreCase);
            if (processedEmails == null || processedEmails.Count == 0)
            {
                return result;
            }

            var keyRows = processedEmails
                .Where(p => !string.IsNullOrWhiteSpace(p.MessageId) && !string.IsNullOrWhiteSpace(p.BookingCode))
                .Select(p => new { MessageId = p.MessageId.Trim(), BookingCode = (p.BookingCode ?? string.Empty).Trim() })
                .GroupBy(x => BuildMessageBookingKey(x.MessageId, x.BookingCode), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (keyRows.Count == 0)
            {
                return result;
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                using var command = connection.CreateCommand();
                var predicates = new List<string>(keyRows.Count);
                for (int i = 0; i < keyRows.Count; i++)
                {
                    var messageParam = $"@cm{i}";
                    var bookingParam = $"@cb{i}";
                    predicates.Add($"(MessageId = {messageParam} AND BookingCode = {bookingParam})");
                    command.Parameters.AddWithValue(messageParam, keyRows[i].MessageId);
                    command.Parameters.AddWithValue(bookingParam, keyRows[i].BookingCode);
                }

                command.CommandText = $@"
SELECT MessageId, BookingCode, Channel, ContactState
FROM dbo.BookingContactChannelState
WHERE {string.Join(" OR ", predicates)}";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var messageId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var bookingCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(bookingCode))
                    {
                        continue;
                    }

                    var channel = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    if (!string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(channel, "wa", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(channel, "platform", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    byte state = 0;
                    if (!reader.IsDBNull(3))
                    {
                        var raw = reader.GetByte(3);
                        state = raw > 3 ? (byte)0 : raw;
                    }

                    var bookingKey = BuildMessageBookingKey(messageId, bookingCode);
                    result.TryGetValue(bookingKey, out var existing);
                    if (string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.smsState = state;
                    }
                    else if (string.Equals(channel, "wa", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.waState = state;
                    }
                    else
                    {
                        existing.platformState = state;
                    }
                    result[bookingKey] = existing;
                }

                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadContactChannelStates: {result.Count} booking keys"); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadContactChannelStates WARN: {ex.Message}"); } catch { }
                return new Dictionary<string, (byte, byte, byte)>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private Dictionary<string, (int customerId, string customerIdentifier)> LoadCustomerIdentityByMessageId(List<string> messageIds)
        {
            var result = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase);
            if (messageIds == null || messageIds.Count == 0) return result;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var paramNames = new List<string>();
                using var command = connection.CreateCommand();
                for (int i = 0; i < messageIds.Count; i++)
                {
                    var name = $"@cid{i}";
                    paramNames.Add(name);
                    command.Parameters.AddWithValue(name, messageIds[i]);
                }

                command.CommandText = $@"
SELECT MessageId, CustomerId, ISNULL(CustomerIdentifier, '') AS CustomerIdentifier, UpdatedAt
FROM dbo.Bookings
WHERE MessageId IN ({string.Join(",", paramNames)})
ORDER BY UpdatedAt DESC, Id DESC;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var messageId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(messageId) || result.ContainsKey(messageId))
                    {
                        continue;
                    }

                    var customerId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    var customerIdentifier = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    result[messageId] = (customerId, customerIdentifier);
                }
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadCustomerIdentity WARN: {ex.Message}"); } catch { }
            }

            return result;
        }

        private Dictionary<string, bool> LoadCheckInByMessageId(List<string> messageIds)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (messageIds == null || messageIds.Count == 0) return result;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var paramNames = new List<string>();
                using var command = connection.CreateCommand();
                for (int i = 0; i < messageIds.Count; i++)
                {
                    var name = $"@ci{i}";
                    paramNames.Add(name);
                    command.Parameters.AddWithValue(name, messageIds[i]);
                }

                command.CommandText = $@"
SELECT MessageId, ISNULL(IsCheckedIn, 0) AS IsCheckedIn, UpdatedAt, Id
FROM dbo.Bookings
WHERE MessageId IN ({string.Join(",", paramNames)})
ORDER BY UpdatedAt DESC, Id DESC";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var messageId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(messageId))
                    {
                        continue;
                    }

                    var isCheckedIn = !reader.IsDBNull(1) && reader.GetBoolean(1);
                    // Take only the latest row for each MessageId (query is ordered by UpdatedAt/Id desc).
                    if (result.ContainsKey(messageId))
                    {
                        continue;
                    }

                    result[messageId] = isCheckedIn;
                }
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadCheckIn WARN: {ex.Message}"); } catch { }
            }

            return result;
        }

        // 2025-12-09 00:00 UTC - Load per-stage message statuses for badges
        private Dictionary<string, List<MessageStageStatus>> LoadMessageStagesByMessageId(List<string> messageIds)
        {
            var result = new Dictionary<string, List<MessageStageStatus>>(StringComparer.OrdinalIgnoreCase);
            if (messageIds == null || messageIds.Count == 0) return result;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var paramNames = new List<string>();
                using var command = connection.CreateCommand();
                for (int i = 0; i < messageIds.Count; i++)
                {
                    var name = $"@m{i}";
                    paramNames.Add(name);
                    command.Parameters.AddWithValue(name, messageIds[i]);
                }

                command.CommandText = $@"SELECT MessageId, Stage, SentFlag, SentAtUtc, Channel, TemplateId, BookingCode, VendorName
                                          FROM BookingMessageStatus
                                          WHERE MessageId IN ({string.Join(",", paramNames)})";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var msgId = reader.GetString(0);
                    var stage = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    var sentFlag = !reader.IsDBNull(2) && reader.GetBoolean(2);
                    var sentAtUtc = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
                    var channel = reader.IsDBNull(4) ? null : reader.GetString(4);
                    var templateId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                    var bookingCode = reader.IsDBNull(6) ? null : reader.GetString(6);
                    var vendorName = reader.IsDBNull(7) ? null : reader.GetString(7);

                    var status = new MessageStageStatus
                    {
                        Stage = stage,
                        SentFlag = sentFlag,
                        SentAtUtc = sentAtUtc,
                        Channel = channel,
                        TemplateId = templateId
                    };

                    if (!result.TryGetValue(msgId, out var list))
                    {
                        list = new List<MessageStageStatus>();
                        result[msgId] = list;
                    }
                    list.Add(status);
                }

                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadMessageStages: {result.Count} msgIds"); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadMessageStages WARN: {ex.Message}"); } catch { }
                return new Dictionary<string, List<MessageStageStatus>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private List<ShapedWalkerData> TransformToShapedWalkers(
            List<ProcessedEmail> processedEmails,
            Dictionary<string, (int? adults, int? children)> adultsChildrenData,
            Dictionary<string, (string? bookingStatus, bool isCancellation, bool isModification, bool isConfirmation, string? emailType)> bookingStatusData,
            Dictionary<string, (bool sent, DateTime? sentAtUtc)> messageSentData,
            Dictionary<string, List<MessageStageStatus>> messageStagesData,
            Dictionary<string, (int customerId, string customerIdentifier)> customerIdentityData,
            Dictionary<string, bool> checkInByMessageId,
            Dictionary<string, (byte smsState, byte waState, byte platformState)> contactStatesByBooking)
        {
            var walkers = new List<ShapedWalkerData>();
            foreach (var email in processedEmails)
            {
                adultsChildrenData.TryGetValue(email.MessageId, out var ac);
                bookingStatusData.TryGetValue(email.MessageId, out var status);
                messageSentData.TryGetValue(email.MessageId, out var sentInfo);
                messageStagesData.TryGetValue(email.MessageId, out var stagesForMessage);
                customerIdentityData.TryGetValue(email.MessageId, out var identity);
                checkInByMessageId.TryGetValue(email.MessageId, out var isCheckedIn);
                var bookingKey = BuildMessageBookingKey(email.MessageId, email.BookingCode);
                contactStatesByBooking.TryGetValue(bookingKey, out var contactStates);
                var (sentFlag, sentAtUtc) = sentInfo;

                var walkerStatus = DetermineWalkerStatus(email, status);
                var statusInfo = GetStatusInfo(walkerStatus);

                // Use ProcessedEmails data as primary source, fallback to Bookings data
                var adults = email.NumberOfAdults ?? ac.adults ?? 0;
                var children = email.NumberOfChildren ?? ac.children ?? 0;
                var attendees = email.NumberOfAttendees ?? 0;
                if (adults == 0 && children == 0 && attendees > 0)
                {
                    adults = attendees;
                }
                var attendeeDisplay = FormatAttendeeDisplay(adults, children);

                var walkerLabel = $"{email.CustomerName ?? "Unknown"} {attendeeDisplay} {email.CustomerPhone ?? string.Empty}".Trim();

                var tourDateDisplay = FormatTourDate(email.TourDate?.ToString("yyyy-MM-dd"));
                var (monthDayOrd, monthName, dayOrd) = BuildDateTokens(email.DisplayDate, email.TourDate);

                walkers.Add(new ShapedWalkerData
                {
                    MessageId = email.MessageId,
                    BookingCode = email.BookingCode ?? string.Empty,
                    CustomerId = identity.customerId,
                    CustomerIdentifier = string.IsNullOrWhiteSpace(identity.customerIdentifier) ? (email.CustomerIdentifier ?? string.Empty) : identity.customerIdentifier,
                    BookingDate = email.CreatedAt,
                    DisplayName = email.CustomerName ?? "Unknown",
                    DisplayAttendees = attendeeDisplay,
                    DisplayPhone = email.CustomerPhone ?? string.Empty,
                    DisplayLabel = walkerLabel,
                    TourName = email.TourName ?? "Unknown Tour",
                    TourDate = tourDateDisplay,
                    TourTime = email.TourTime ?? string.Empty,
                    TourDayOfWeek = email.TourDayOfWeek ?? string.Empty,
                    DisplayDate = email.DisplayDate ?? string.Empty,
                    DisplayTime = email.DisplayTime ?? string.Empty,
                    TourMonthAndDayOrdinal = monthDayOrd ?? string.Empty,
                    MonthOfTour = monthName ?? string.Empty,
                    DisplayDayOrdinal = dayOrd ?? string.Empty,
                    MeetingPlace = email.TourLocation ?? string.Empty,
                    MeetingTime = email.TourTime ?? email.DisplayTime ?? string.Empty,
                    MeetingInstructions = string.Empty,
                    TourStartTime = email.TourTime ?? string.Empty,
                    TourLocation = email.TourLocation ?? string.Empty,
                    VendorName = string.IsNullOrWhiteSpace(email.VendorName) ? "UNKNOWN VENDOR" : email.VendorName.ToUpperInvariant(),
                    Status = walkerStatus,
                    StatusIcon = statusInfo.icon,
                    StatusClass = statusInfo.cssClass,
                    StatusTitle = statusInfo.title,
                    Attendees = adults + children,
                    NumberOfAdults = adults,
                    NumberOfChildren = children,
                    Language = email.Language ?? string.Empty,
                    EmailType = email.EmailType ?? string.Empty,
                    IsCancellation = status.isCancellation || email.IsCancellation,
                    IsModification = status.isModification || email.IsModification,
                    IsConfirmation = status.isConfirmation || email.IsBooking,
                    IsCheckedIn = isCheckedIn,
                    MessageSent = sentFlag,
                    MessageSentAtUtc = sentAtUtc,
                    SmsContactState = contactStates.smsState,
                    WaContactState = contactStates.waState,
                    PlatformContactState = contactStates.platformState,
                    MessageStages = (stagesForMessage ?? new List<MessageStageStatus>())
                        .GroupBy(s => s.Stage ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SentAtUtc ?? DateTime.MinValue).First(), StringComparer.OrdinalIgnoreCase)
                });
            }
            return walkers;
        }

        private static string BuildMessageBookingKey(string? messageId, string? bookingCode)
        {
            return $"{(messageId ?? string.Empty).Trim()}||{(bookingCode ?? string.Empty).Trim()}";
        }

        /// <summary>
        /// Pre-load tour name -> master name mappings for all unique tour names in the walker set
        /// Created: 2026-02-11
        /// </summary>
        private async Task<Dictionary<string, TourNameResolution>> BuildTourNameMappingCacheAsync(List<ShapedWalkerData> walkers)
        {
            var cache = new Dictionary<string, TourNameResolution>(StringComparer.OrdinalIgnoreCase);

            // Get unique tour names from walkers
            var uniqueTourNames = walkers
                .Select(w => new { TourName = w.TourName ?? string.Empty, VendorName = w.VendorName ?? string.Empty })
                .Where(x => !string.IsNullOrWhiteSpace(x.TourName))
                .Distinct()
                .ToList();

#if DEBUG
            Console.WriteLine($"[TourTreeDataProviderSqlServer] Resolving {uniqueTourNames.Count} unique tour names");
#endif

            // Resolve each unique tour name to master name
            foreach (var item in uniqueTourNames)
            {
                var resolution = await _tourNameNormalizer.ResolveToMasterTourAsync(item.TourName, item.VendorName);
                if (resolution != null)
                {
                    // Use normalized tour name as key for consistency with old grouping logic
                    var normalizedKey = TourNameNormalization.NormalizeTourNameForGrouping(item.VendorName, item.TourName);
                    cache[normalizedKey] = resolution;
                }
                else
                {
                    // Fallback: use title-cased normalized name as master name
                    var normalizedName = TourNameNormalization.NormalizeTourNameForGrouping(item.VendorName, item.TourName);
                    var displayName = TourNameNormalization.NormalizeTourNameForDisplay(item.TourName);
                    cache[normalizedName] = new TourNameResolution
                    {
                        TourId = 0, // Unknown
                        MasterTourName = displayName,
                        MasterTourNameDesktop = null,
                        MasterTourNameMobile = null
                    };
                }
            }

            return cache;
        }

        private ReportGroupStatusKeys BuildReportGroupStatusKeys(
            DateTime? startDate,
            DateTime? endDate,
            Dictionary<string, TourNameResolution> tourNameMappings)
        {
            var keys = new ReportGroupStatusKeys();
            var reports = LoadTourReportStatusRows(startDate, endDate);
            if (reports.Count == 0)
            {
                return keys;
            }

            foreach (var report in reports)
            {
                var masterName = ResolveMasterTourNameForGrouping(tourNameMappings, vendorName: null, report.TourName);
                var familyKey = NormalizeTourFamilyKey(masterName);
                var timeKey = NormalizeTimeKey(report.TourTime);
                var groupKey = BuildReportGroupKey(report.TourDate.Date, timeKey, familyKey);

                if (report.IsSubmitted)
                {
                    keys.SubmittedKeys.Add(groupKey);

                    var publicId = (report.PublicId ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(publicId))
                    {
                        keys.GalleryAvailableKeys.Add(groupKey);
                        if (!keys.GalleryPublicIdByKey.ContainsKey(groupKey))
                        {
                            keys.GalleryPublicIdByKey[groupKey] = publicId;
                        }
                    }
                }
                else
                {
                    keys.UnsubmittedKeys.Add(groupKey);
                }
            }

            return keys;
        }

        private List<TourReportStatusRow> LoadTourReportStatusRows(DateTime? startDate, DateTime? endDate)
        {
            var rows = new List<TourReportStatusRow>();

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var sql = @"
SELECT TourDate, TourName, TourTime, IsSubmitted, PublicId
FROM dbo.TourReports
WHERE IsSubmitted IN (0, 1)";

                if (startDate.HasValue)
                {
                    sql += " AND TourDate >= @start";
                }
                if (endDate.HasValue)
                {
                    sql += " AND TourDate < @end";
                }

                using var command = new SqlCommand(sql, connection);
                if (startDate.HasValue) command.Parameters.AddWithValue("@start", startDate.Value.Date);
                if (endDate.HasValue) command.Parameters.AddWithValue("@end", endDate.Value.Date);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new TourReportStatusRow
                    {
                        TourDate = reader.GetDateTime(0),
                        TourName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        TourTime = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        IsSubmitted = !reader.IsDBNull(3) && reader.GetBoolean(3),
                        PublicId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
                    });
                }
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadTourReportStatusRows WARN: {ex.Message}"); } catch { }
            }

            return rows;
        }

        private static string ResolveMasterTourNameForGrouping(
            Dictionary<string, TourNameResolution> tourNameMappings,
            string? vendorName,
            string? tourName)
        {
            var normalizedWithVendor = TourNameNormalization.NormalizeTourNameForGrouping(vendorName ?? string.Empty, tourName ?? string.Empty);
            if (tourNameMappings.TryGetValue(normalizedWithVendor, out var mappedByVendor))
            {
                return mappedByVendor.MasterTourNameDesktop ?? mappedByVendor.MasterTourName;
            }

            var normalizedWithoutVendor = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, tourName ?? string.Empty);
            if (tourNameMappings.TryGetValue(normalizedWithoutVendor, out var mappedWithoutVendor))
            {
                return mappedWithoutVendor.MasterTourNameDesktop ?? mappedWithoutVendor.MasterTourName;
            }

            return normalizedWithoutVendor;
        }

        private static string NormalizeTourFamilyKey(string? tourName)
        {
            var familyKey = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, tourName ?? string.Empty);
            return string.IsNullOrWhiteSpace(familyKey) ? "__unknown__" : familyKey;
        }

        private static string BuildReportGroupKey(DateTime tourDate, string timeKey, string familyKey)
        {
            return $"{tourDate:yyyy-MM-dd}|{timeKey}|{familyKey}";
        }

        private ShapedTreeData BuildShapedTreeStructure(
            List<ShapedWalkerData> walkers,
            List<TourGuideAssignment> assignments,
            Dictionary<string, TourNameResolution> tourNameMappings,
            ReportGroupStatusKeys reportGroupStatusKeys)
        {
            var treeData = new ShapedTreeData
            {
                TotalBookings = walkers.Count,
                TotalWalkers = walkers.Count,
                GeneratedAt = DateTime.UtcNow
            };

            var dateGroups = walkers
                .GroupBy(w => DateTime.TryParse(w.TourDate, out var d) ? d.Date : DateTime.MaxValue)
                .OrderBy(g => g.Key);

            foreach (var dateGroup in dateGroups)
            {
                var dateLabel = dateGroup.Key == DateTime.MaxValue ? "Unknown Date" : dateGroup.Key.ToString("dddd, MMM d, yyyy");

                // Calculate total confirmed guests for the day
                var dailyGuestCount = dateGroup
                    .Where(w => w.Status == "Confirmed")
                    .Sum(w => w.Attendees);

                var dateNode = new ShapedTreeNode
                {
                    Label = dateLabel,
                    Icon = "bi-calendar-event",
                    NodeType = "Date",
                    IsExpanded = true,
                    DailyGuestCount = dailyGuestCount > 0 ? dailyGuestCount : null // Assign the count here
                };

                // Group tours by master tour name + normalized time
                // 2026-02-11 - Use master tour name from DB mappings instead of normalized variant name
                var tourGroups = dateGroup
                    .GroupBy(w =>
                    {
                        var normalizedKey = TourNameNormalization.NormalizeTourNameForGrouping(w.VendorName ?? string.Empty, w.TourName ?? string.Empty);
                        var masterName = normalizedKey; // fallback
                        
                        if (tourNameMappings.TryGetValue(normalizedKey, out var resolution))
                        {
                            // Use MasterTourNameDesktop if available, otherwise MasterTourName
                            masterName = resolution.MasterTourNameDesktop ?? resolution.MasterTourName;
                            Console.WriteLine($"[BuildTree] Found mapping: '{w.TourName}' -> '{masterName}'");
                        }
                        else
                        {
                            Console.WriteLine($"[BuildTree] No mapping for '{w.TourName}' (key: '{normalizedKey}')");
                        }
                        
                        return new
                        {
                            Name = masterName,
                            TimeKey = NormalizeTimeKey(w.TourTime)
                        };
                    })
                    // 2025-12-05 00:00 UTC - sort by time-of-day first, then name for consistent chronological display
                    .OrderBy(g => g.Key.TimeKey)
                    .ThenBy(g => g.Key.Name);

                foreach (var tourGroup in tourGroups)
                {
                    var tourName = tourGroup.Key.Name; // This is now the master tour name!
                    Console.WriteLine($"[BuildTree] Creating tour node with name: '{tourName}'");
                    var sampleWalker = tourGroup.FirstOrDefault();
                    var timeDisplay = NormalizeTimeDisplay(sampleWalker?.TourTime);
                    var familyKey = NormalizeTourFamilyKey(tourName);
                    var reportGroupKey = BuildReportGroupKey(dateGroup.Key, tourGroup.Key.TimeKey, familyKey);
                    var hasSubmittedReportForTourGroup = dateGroup.Key != DateTime.MaxValue &&
                        reportGroupStatusKeys.SubmittedKeys.Contains(reportGroupKey);
                    var hasUnsubmittedReportForTourGroup = !hasSubmittedReportForTourGroup &&
                        dateGroup.Key != DateTime.MaxValue &&
                        reportGroupStatusKeys.UnsubmittedKeys.Contains(reportGroupKey);
                    var hasGuideGalleryInDbForTourGroup = hasSubmittedReportForTourGroup &&
                        reportGroupStatusKeys.GalleryAvailableKeys.Contains(reportGroupKey);
                    var guideReportPublicIdForTourGroup =
                        reportGroupStatusKeys.GalleryPublicIdByKey.TryGetValue(reportGroupKey, out var groupPublicId)
                            ? groupPublicId
                            : string.Empty;
                    var tourLabel = string.IsNullOrWhiteSpace(timeDisplay) ? tourName : $"{tourName} \u2014 {timeDisplay}";
                    Console.WriteLine($"[BuildTree] Final tourLabel for tree: '{tourLabel}'");
                    var mobileBaseName = tourName;
                    if (sampleWalker != null)
                    {
                        var normalizedKey = TourNameNormalization.NormalizeTourNameForGrouping(sampleWalker.VendorName ?? string.Empty, sampleWalker.TourName ?? string.Empty);
                        if (tourNameMappings.TryGetValue(normalizedKey, out var resolution))
                        {
                            if (!string.IsNullOrWhiteSpace(resolution.MasterTourNameMobile))
                            {
                                mobileBaseName = resolution.MasterTourNameMobile;
                            }
                        }
                    }
                    var tourMobileLabel = string.IsNullOrWhiteSpace(timeDisplay) ? mobileBaseName : $"{mobileBaseName} \u2014 {timeDisplay}";

                    var tourNode = new ShapedTreeNode
                    {
                        Label = tourLabel, // canonical name + time
                        MobileLabel = tourMobileLabel,
                        Icon = "bi-geo-alt",
                        NodeType = "Tour",
                        IsExpanded = true
                    };

                    // 2026-01-31 - Attempt to find assigned guide
                    if (assignments != null && dateGroup.Key != DateTime.MaxValue)
                    {
                        var match = MatchAssignment(assignments, dateGroup.Key, tourName, tourGroup.FirstOrDefault()?.TourTime);
                        if (match != null)
                        {
                            tourNode.AssignedGuideId = match.GuideId;
                            // Optionally fetch name if we had a guide lookup. For now ID is enough for the UI to select it from list.
                        }
                    }

                    var vendorGroups = tourGroup
                        .GroupBy(w => string.IsNullOrWhiteSpace(w.VendorName) ? "UNKNOWN VENDOR" : w.VendorName.Trim().ToUpperInvariant())
                        .OrderBy(g => g.Key);

                    foreach (var vendorGroup in vendorGroups)
                    {
                        var vendorNode = new ShapedTreeNode
                        {
                            Label = vendorGroup.Key,
                            Icon = "bi-shop",
                            NodeType = "Vendor",
                            IsExpanded = true
                        };

                        // 2026-01-08 - Order by booking date (newest first, oldest at bottom) instead of name
                    foreach (var walker in vendorGroup.OrderByDescending(w => w.BookingDate ?? DateTime.MinValue))
                        {
                            walker.HasSubmittedGuideReport = hasSubmittedReportForTourGroup;
                            walker.HasUnsubmittedGuideReport = hasUnsubmittedReportForTourGroup;
                            walker.HasGuideGalleryInDb = hasGuideGalleryInDbForTourGroup;
                            walker.GuideReportPublicId = guideReportPublicIdForTourGroup;
                            var walkerNode = new ShapedTreeNode
                            {
                                Label = walker.DisplayLabel,
                                Icon = "bi-person",
                                NodeType = "Walker",
                                IsExpanded = true,
                                WalkerData = walker
                            };
                            vendorNode.Children.Add(walkerNode);
                        }

                        tourNode.Children.Add(vendorNode);
                    }

                    dateNode.Children.Add(tourNode);
                }

                // 2025-11-23 00:00 UTC - Verbatim merge logic (shaped)
                // 2026-02-11 - DISABLED: We now group by master tour names directly, so this normalization
                // is redundant and was overwriting our Title Case labels with lowercase normalized names
                // NormalizeAndMergeShapedTree(dateNode);

                treeData.TreeNodes.Add(dateNode);
            }

            treeData.VendorCounts = walkers.GroupBy(w => w.VendorName).ToDictionary(g => g.Key, g => g.Count());
            // 2026-02-11 - Aggregate by master tour name to match rendered grouping
            treeData.TourCounts = walkers
                .GroupBy(w =>
                {
                    var normalizedKey = TourNameNormalization.NormalizeTourNameForGrouping(w.VendorName ?? string.Empty, w.TourName ?? string.Empty);
                    if (tourNameMappings.TryGetValue(normalizedKey, out var resolution))
                    {
                        return resolution.MasterTourNameDesktop ?? resolution.MasterTourName;
                    }
                    return normalizedKey; // fallback
                })
                .ToDictionary(g => g.Key, g => g.Count());
            return treeData;
        }

        /// <summary>
        /// Merge duplicate tour nodes under a date node by normalized tour label + time.
        /// Keeps separate branches for same-name different-time tours.
        /// Created: 2025-11-23 00:00 UTC
        /// </summary>
        private static void NormalizeAndMergeShapedTree(ShapedTreeNode dateNode)
        {
            if (dateNode?.Children == null || dateNode.Children.Count == 0) return;

            // Build map of normalized (name + time) label -> existing tour node
            var normalizedToNode = new Dictionary<string, ShapedTreeNode>(StringComparer.OrdinalIgnoreCase);
            var toRemove = new List<ShapedTreeNode>();

            foreach (var tourNode in dateNode.Children)
            {
                var (rawName, timePart) = SplitNameAndTime(tourNode.Label ?? string.Empty);
                var normalizedName = TourNameNormalization.NormalizeTourNameForGrouping(string.Empty, rawName);
                var normalized = string.IsNullOrWhiteSpace(timePart) ? normalizedName : $"{normalizedName} \u2014 {timePart}";
                if (normalizedToNode.TryGetValue(normalized, out var existing))
                {
                    // Merge vendors under existing canonical node
                    if (tourNode.Children != null && tourNode.Children.Count > 0)
                    {
                        // Group vendors by label to avoid duplicates (case-insensitive)
                        var vendorGroups = tourNode.Children.GroupBy(v => v.Label ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                        foreach (var vendorGroup in vendorGroups)
                        {
                            var vendorName = vendorGroup.Key;
                            var existingVendor = existing.Children.FirstOrDefault(v =>
                                string.Equals(v.Label, vendorName, StringComparison.OrdinalIgnoreCase));

                            if (existingVendor != null)
                            {
                                foreach (var vendorNode in vendorGroup)
                                {
                                    if (vendorNode.Children != null && vendorNode.Children.Count > 0)
                                    {
                                        existingVendor.Children.AddRange(vendorNode.Children);
                                    }
                                }
                            }
                            else
                            {
                                existing.Children.AddRange(vendorGroup);
                            }
                        }
                    }
                    toRemove.Add(tourNode);
                }
                else
                {
                    // Display canonicalized name while preserving time
                    tourNode.Label = normalized;
                    normalizedToNode[normalized] = tourNode;
                }
            }

            // Remove duplicate tour nodes
            foreach (var node in toRemove)
            {
                dateNode.Children.Remove(node);
            }
        }

        private static (string name, string time) SplitNameAndTime(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return (string.Empty, string.Empty);
            var separators = new[] { " \u2014 ", " \u2013 ", " - ", " \u00E2\u20AC\u201D ", " \u00E2\u20AC\u201C ", " \u00C3\u00A2\u00E2\u201A\u00AC\u00E2\u20AC\u009D ", " \u00C3\u00A2\u00E2\u201A\u00AC\u00E2\u20AC\u015C " };
            foreach (var separator in separators)
            {
                var idx = label.LastIndexOf(separator, StringComparison.Ordinal);
                if (idx <= 0)
                {
                    continue;
                }

                var name = label[..idx].Trim();
                var time = label[(idx + separator.Length)..].Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return (name, time);
                }
            }
            return (label.Trim(), string.Empty);
        }

        private static string NormalizeTimeKey(string? rawTime)
        {
            if (string.IsNullOrWhiteSpace(rawTime)) return "99:99"; // sort TBA after real times
            var s = rawTime.Trim();
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                return dt.ToString("HH:mm");
            }
            if (TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var ts) ||
                TimeSpan.TryParse(s, out ts))
            {
                var dt2 = DateTime.Today.Add(ts);
                return dt2.ToString("HH:mm");
            }
            return "98:98"; // unknown but present; still after real times
        }

        private static string NormalizeTimeDisplay(string? rawTime)
        {
            if (string.IsNullOrWhiteSpace(rawTime)) return "Time TBA";
            var s = rawTime.Trim();
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            {
                return dt.ToString("h:mm tt");
            }
            if (TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var ts) ||
                TimeSpan.TryParse(s, out ts))
            {
                var dt2 = DateTime.Today.Add(ts);
                return dt2.ToString("h:mm tt");
            }
            return s;
        }

        private string DetermineWalkerStatus(ProcessedEmail email, (string? bookingStatus, bool isCancellation, bool isModification, bool isConfirmation, string? emailType) status)
        {
            // 2025-12-02 00:00 UTC - Require explicit cancellation email; do not infer from BookingStatus alone.
            if (status.isCancellation || email.IsCancellation ||
                (email.EmailType?.Equals("cancellation", StringComparison.OrdinalIgnoreCase) == true))
                return "Cancelled";

            if (status.isModification || email.IsModification ||
                (email.EmailType?.Equals("modification", StringComparison.OrdinalIgnoreCase) == true))
            {
                // In confirmations-only view, treat modifications as confirmed active bookings
                if (_currentFilterType == TreeDataFilterType.ConfirmationsOnly)
                    return "Confirmed";
                return "Modified";
            }

            if (status.isConfirmation || email.IsBooking ||
                (email.EmailType?.Equals("booking", StringComparison.OrdinalIgnoreCase) == true))
                return "Confirmed";

            return "Pending";
        }

        private (string icon, string cssClass, string title) GetStatusInfo(string status)
        {
            switch (status)
            {
                case "Cancelled": return ("bi-x-circle-fill", "cancelled", "Cancelled");
                case "Modified": return ("bi-pencil-square", "modified", "Modified");
                case "Confirmed": return ("bi-check-circle-fill", "confirmed", "Confirmed");
                case "Pending": return ("bi-clock", "pending", "Pending");
                default: return ("bi-question-circle", "unknown", "Unknown");
            }
        }

        private string FormatAttendeeDisplay(int adults, int children)
        {
            if (adults > 0 && children > 0) return $"{adults}.{children}";
            if (adults > 0) return adults.ToString();
            if (children > 0) return $"0.{children}";
            return "0";
        }

        private string FormatTourDate(string? tourDate)
        {
            if (string.IsNullOrWhiteSpace(tourDate)) return "Unknown Date";
            if (DateTime.TryParse(tourDate, out var d)) return d.ToString("dddd, MMM d, yyyy");
            return tourDate;
        }

        // 2025-12-10 00:00 UTC - Ordinal/month helpers for messaging tokens
        private static string GetDayOrdinal(int day)
        {
            if (day <= 0) return string.Empty;
            if (day % 100 is 11 or 12 or 13) return $"{day}th";
            return (day % 10) switch
            {
                1 => $"{day}st",
                2 => $"{day}nd",
                3 => $"{day}rd",
                _ => $"{day}th"
            };
        }

        private static (string? monthAndDayOrdinal, string? monthName, string? dayOrdinal) BuildDateTokens(string? displayDate, DateTime? tourDate)
        {
            DateTime? source = null;
            if (!string.IsNullOrWhiteSpace(displayDate) && DateTime.TryParse(displayDate, out var parsedDisplay))
            {
                source = parsedDisplay;
            }
            else if (tourDate.HasValue)
            {
                source = tourDate.Value;
            }

            if (!source.HasValue) return (null, null, null);

            var dt = source.Value;
            var monthName = dt.ToString("MMMM");
            var dayOrd = GetDayOrdinal(dt.Day);
            return ($"{monthName} {dayOrd}", monthName, dayOrd);
        }

        private ProcessedEmail MapReaderToProcessedEmail(SqlDataReader reader)
        {
            return new ProcessedEmail
            {
                Id = reader.GetInt32(0),
                InboxEmailId = reader.GetInt32(1),
                MessageId = reader.GetString(2),
                VendorName = reader.GetString(3),
                EmailType = reader.GetString(4),
                IsTourBookingEmail = reader.GetBoolean(5),
                ClassificationRuleId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ProcessingStatus = reader.GetString(7),
                ProcessingStartedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                ProcessingCompletedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                ProcessingError = reader.IsDBNull(10) ? null : reader.GetString(10),
                ProcessingAttempts = reader.GetInt32(11),
                NextProcessingAttempt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                RateLimitResetAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                CustomerName = reader.IsDBNull(14) ? null : reader.GetString(14),
                BookingCode = reader.IsDBNull(15) ? null : reader.GetString(15),
                CustomerPhone = reader.IsDBNull(16) ? null : reader.GetString(16),
                CustomerEmail = reader.IsDBNull(17) ? null : reader.GetString(17),
                NumberOfAttendees = reader.IsDBNull(18) ? null : reader.GetInt32(18),
                NumberOfAdults = reader.IsDBNull(19) ? null : reader.GetInt32(19),
                NumberOfChildren = reader.IsDBNull(20) ? null : reader.GetInt32(20),
                Language = reader.IsDBNull(21) ? null : reader.GetString(21),
                TourDate = reader.IsDBNull(22) ? null : DateTime.TryParse(reader.GetString(22), out var td) ? td : null,
                TourTime = reader.IsDBNull(23) ? null : reader.GetString(23),
                TourName = reader.IsDBNull(24) ? null : reader.GetString(24),
                TourLocation = reader.IsDBNull(25) ? null : reader.GetString(25),
                ExtractedAt = reader.IsDBNull(26) ? null : reader.GetDateTime(26),
                CustomerIdentifier = reader.IsDBNull(27) ? null : reader.GetString(27),
                IsLatestAction = reader.GetBoolean(28),
                CreatedAt = reader.GetDateTime(29),
                UpdatedAt = reader.GetDateTime(30),
                ManualParsingCompleted = reader.GetBoolean(31),
                ManualParsingNotes = reader.IsDBNull(32) ? null : reader.GetString(32),
                ManualParsingTimestamp = reader.IsDBNull(33) ? null : reader.GetDateTime(33),
                PlainTextContent = reader.IsDBNull(34) ? null : reader.GetString(34),
                IsCancellation = reader.GetBoolean(35),
                IsModification = reader.GetBoolean(36),
                IsBooking = reader.GetBoolean(37),
                AssociatedBookingIds = reader.IsDBNull(38) ? new List<string>() : System.Text.Json.JsonSerializer.Deserialize<List<string>>(reader.GetString(38)) ?? new List<string>(),
                BookingAlterationNotes = reader.IsDBNull(39) ? null : reader.GetString(39),
                HtmlContent = reader.IsDBNull(40) ? null : reader.GetString(40),
                ExtractedBookingCode = reader.IsDBNull(41) ? null : reader.GetString(41),
                NewBookingCode = reader.IsDBNull(42) ? null : reader.GetString(42),
                PreviousBookingCode = reader.IsDBNull(43) ? null : reader.GetString(43),
                TourDayOfWeek = reader.IsDBNull(44) ? null : reader.GetString(44),
                DisplayDate = reader.IsDBNull(45) ? null : reader.GetString(45),
                DisplayTime = reader.IsDBNull(46) ? null : reader.GetString(46)
            };
        }

        // 2026-01-31 - Fetch guide assignments
        private List<TourGuideAssignment> LoadTourGuideAssignments(DateTime? startDate, DateTime? endDate)
        {
            var list = new List<TourGuideAssignment>();
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var sql = "SELECT Id, TourDate, TourName, TourTime, GuideId FROM TourGuideAssignments WHERE 1=1";
                if (startDate.HasValue)
                {
                    sql += " AND TourDate >= @start";
                }
                if (endDate.HasValue)
                {
                    sql += " AND TourDate < @end";
                }

                using var command = new SqlCommand(sql, connection);
                if (startDate.HasValue) command.Parameters.AddWithValue("@start", startDate.Value.Date);
                if (endDate.HasValue) command.Parameters.AddWithValue("@end", endDate.Value.Date);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new TourGuideAssignment
                    {
                        Id = reader.GetInt32(0),
                        TourDate = reader.GetDateTime(1),
                        TourName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        TourTime = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        GuideId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
                    });
                }
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadTourGuideAssignments WARN: {ex.Message}"); } catch { }
            }
            return list;
        }

        private TourGuideAssignment? MatchAssignment(List<TourGuideAssignment> assignments, DateTime date, string tourName, string? tourTime)
        {
            // Simple match: date + tourName (normalized) + approximate time?
            // Assignments from DB likely have non-normalized names/times.
            // We'll try exact date match first.
            var candidates = assignments.Where(a => a.TourDate.Date == date.Date).ToList();
            if (!candidates.Any()) return null;

            var normTourName = TourNameNormalization.NormalizeTourNameForGrouping("", tourName);
            var normTime = NormalizeTimeKey(tourTime);

            // Try to find best match
            // 1. Name matches + Time matches
            foreach (var c in candidates)
            {
                // Normalize candidate
                var cName = TourNameNormalization.NormalizeTourNameForGrouping("", c.TourName);
                var cTime = NormalizeTimeKey(c.TourTime);

                if (string.Equals(cName, normTourName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(cTime, normTime, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
            
            // 2. Name matches (ignore time if only one tour of that name on that day?) 
            // Avoid ambiguity if multiple tours of same name exist.
            var nameMatches = candidates.Where(c => 
                string.Equals(TourNameNormalization.NormalizeTourNameForGrouping("", c.TourName), normTourName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nameMatches.Count == 1) return nameMatches[0];

            return null;
        }
        private sealed class TourReportStatusRow
        {
            public DateTime TourDate { get; set; }
            public string TourName { get; set; } = string.Empty;
            public string TourTime { get; set; } = string.Empty;
            public bool IsSubmitted { get; set; }
            public string PublicId { get; set; } = string.Empty;
        }

        private sealed class ReportGroupStatusKeys
        {
            public HashSet<string> SubmittedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> UnsubmittedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> GalleryAvailableKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> GalleryPublicIdByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private class ScheduleMatchInfo
        {
            public DbTourSchedule Schedule { get; set; } = new();
            public string TourName { get; set; } = string.Empty;
        }

        private enum ScheduleOverrideMatchQuality
        {
            TourIdMatch = 1,
            NameFallbackMatch = 2,
            Unresolved = 3
        }

        private sealed class ScheduleOverrideDiagnostics
        {
            private const int MaxUnresolvedSamples = 8;
            private readonly List<string> _unresolvedSamples = new();

            public int TotalWalkers { get; set; }
            public int ParsedDateRows { get; set; }
            public int InvalidDateRows { get; set; }
            public int ResolvedTourIdRows { get; set; }
            public int TourIdMatches { get; set; }
            public int NameFallbackMatches { get; set; }
            public int UnresolvedMatches { get; set; }

            public void RecordInvalidDate()
            {
                InvalidDateRows++;
            }

            public void RecordParsedDate(int resolvedTourId)
            {
                ParsedDateRows++;
                if (resolvedTourId > 0)
                {
                    ResolvedTourIdRows++;
                }
            }

            public void RecordMatch(
                ScheduleOverrideMatchQuality quality,
                ShapedWalkerData walker,
                DateTime tourDate,
                int resolvedTourId,
                int potentialScheduleCount)
            {
                switch (quality)
                {
                    case ScheduleOverrideMatchQuality.TourIdMatch:
                        TourIdMatches++;
                        break;
                    case ScheduleOverrideMatchQuality.NameFallbackMatch:
                        NameFallbackMatches++;
                        break;
                    default:
                        UnresolvedMatches++;
                        if (_unresolvedSamples.Count < MaxUnresolvedSamples)
                        {
                            _unresolvedSamples.Add(
                                $"{tourDate:yyyy-MM-dd} | Vendor='{walker.VendorName}' | Tour='{walker.TourName}' | Time='{walker.TourTime}' | Booking='{walker.BookingCode}' | ResolvedTourId={resolvedTourId} | PotentialSchedules={potentialScheduleCount}");
                        }

                        break;
                }
            }

            public void WriteSummary(int scheduleCount)
            {
                try
                {
                    Console.WriteLine(
                        $"[TourTreeDataProviderSqlServer] Schedule override match-quality: Walkers={TotalWalkers}, ParsedDates={ParsedDateRows}, InvalidDates={InvalidDateRows}, SchedulesLoaded={scheduleCount}, ResolvedTourIdRows={ResolvedTourIdRows}, TourIdMatch={TourIdMatches}, NameFallbackMatch={NameFallbackMatches}, Unresolved={UnresolvedMatches}");

                    if (_unresolvedSamples.Count > 0)
                    {
                        Console.WriteLine("[TourTreeDataProviderSqlServer] Schedule override unresolved samples (top 8):");
                        foreach (var sample in _unresolvedSamples)
                        {
                            Console.WriteLine($"  - {sample}");
                        }
                    }
                }
                catch
                {
                    // no-op: diagnostics are best-effort only
                }
            }
        }

        private List<ScheduleMatchInfo> LoadTourSchedules(DateTime? startDate, DateTime? endDate)
        {
            var list = new List<ScheduleMatchInfo>();
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var sql = @"
                    SELECT ts.Id, ts.TourId, ts.ScheduleName, ts.StartDate, ts.EndDate, 
                           ts.MeetingPlace, ts.MeetingTime, ts.MeetingInstructions, ts.VendorLink,
                           ts.DayAssignmentsJson, ts.TimeSlotsJson,
                           t.TourName
                    FROM TourSchedules ts
                    JOIN Tours t ON ts.TourId = t.Id
                    WHERE ts.IsActive = 1";

                if (startDate.HasValue) sql += " AND ts.EndDate >= @start";
                if (endDate.HasValue) sql += " AND ts.StartDate <= @end"; // Fix: Check overlap correctly (Start <= End AND End >= Start)

                using var command = new SqlCommand(sql, connection);
                if (startDate.HasValue) command.Parameters.AddWithValue("@start", startDate.Value.Date);
                if (endDate.HasValue) command.Parameters.AddWithValue("@end", endDate.Value.Date);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var s = new DbTourSchedule
                    {
                        Id = reader.GetInt32(0),
                        TourId = reader.GetInt32(1),
                        ScheduleName = reader.GetString(2),
                        StartDate = reader.GetDateTime(3),
                        EndDate = reader.GetDateTime(4),
                        MeetingPlace = reader.IsDBNull(5) ? null : reader.GetString(5),
                        MeetingTime = reader.IsDBNull(6) ? null : reader.GetString(6),
                        MeetingInstructions = reader.IsDBNull(7) ? null : reader.GetString(7),
                        VendorLink = reader.IsDBNull(8) ? null : reader.GetString(8),
                        DayAssignmentsJson = reader.IsDBNull(9) ? null : reader.GetString(9),
                        TimeSlotsJson = reader.IsDBNull(10) ? null : reader.GetString(10)
                    };
                    var tourName = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);

                    list.Add(new ScheduleMatchInfo { Schedule = s, TourName = tourName });
                }
            }
            catch (Exception ex)
            {
                try { Console.WriteLine($"[TourTreeDataProviderSqlServer] LoadTourSchedules WARN: {ex.Message}"); } catch { }
            }
            return list;
        }

        private void ApplyScheduleOverrides(
            List<ShapedWalkerData> walkers,
            List<ScheduleMatchInfo> schedules,
            Dictionary<string, TourNameResolution> tourNameMappings)
        {
            if (walkers == null || walkers.Count == 0) return;

            var diagnostics = new ScheduleOverrideDiagnostics
            {
                TotalWalkers = walkers.Count
            };

            var activeSchedules = schedules ?? new List<ScheduleMatchInfo>();

            foreach (var walker in walkers)
            {
                // Try to parse walker date
                if (!DateTime.TryParse(walker.TourDate, out var wDate))
                {
                    diagnostics.RecordInvalidDate();
                    continue;
                }

                var walkerTourNameNorm = TourNameNormalization.NormalizeTourNameForGrouping("", walker.TourName ?? string.Empty);
                var walkerTourKey = TourNameNormalization.NormalizeTourNameForGrouping(walker.VendorName ?? string.Empty, walker.TourName ?? string.Empty);
                var resolvedTourId = 0;
                if (tourNameMappings.TryGetValue(walkerTourKey, out var resolution) &&
                    resolution.TourId > 0)
                {
                    resolvedTourId = resolution.TourId;
                }

                diagnostics.RecordParsedDate(resolvedTourId);

                // Filter by date overlap first
                var potentialSchedules = activeSchedules
                    .Where(x => wDate.Date >= x.Schedule.StartDate.Date && wDate.Date <= x.Schedule.EndDate.Date)
                    .ToList();

                ScheduleMatchInfo? match = null;
                var matchQuality = ScheduleOverrideMatchQuality.Unresolved;

                // Prefer strict TourId matching to avoid ambiguous same-name tours.
                if (resolvedTourId > 0)
                {
                    match = potentialSchedules.FirstOrDefault(x => x.Schedule.TourId == resolvedTourId);
                    if (match != null)
                    {
                        matchQuality = ScheduleOverrideMatchQuality.TourIdMatch;
                    }
                }

                // Transitional fallback for unresolved mappings.
                if (match == null)
                {
                    match = potentialSchedules.FirstOrDefault(x =>
                        string.Equals(TourNameNormalization.NormalizeTourNameForGrouping("", x.TourName), walkerTourNameNorm, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        matchQuality = ScheduleOverrideMatchQuality.NameFallbackMatch;
                    }
                }

                diagnostics.RecordMatch(matchQuality, walker, wDate, resolvedTourId, potentialSchedules.Count);

                if (match != null)
                {
                    var sched = match.Schedule;
                    string? overrideMeetingTime = null;
                    string? overrideMeetingPlace = null;
                    string? overrideInstructions = null;
                    string? overrideLink = null;

                    // Check for specific time slot override
                    bool slotFound = false;
                    if (!string.IsNullOrWhiteSpace(sched.TimeSlotsJson))
                    {
                        try
                        {
                            var slots = System.Text.Json.JsonSerializer.Deserialize<List<TimeSlotModel>>(sched.TimeSlotsJson);
                            if (slots != null)
                            {
                                var wTimeNorm = NormalizeTimeKey(walker.TourTime);
                                var slot = slots.FirstOrDefault(s => NormalizeTimeKey(s.TourTime) == wTimeNorm);
                                if (slot != null)
                                {
                                    slotFound = true;
                                    overrideMeetingTime = slot.MeetingTime;
                                    overrideMeetingPlace = slot.MeetingPlace;
                                    overrideInstructions = slot.MeetingInstructions;
                                    overrideLink = slot.VendorLink;
                                }
                            }
                        }
                        catch { /* ignore json error */ }
                    }

                    // Fallback to schedule global defaults if not found in slot OR if slot values are empty (optional, depending on business rule. Usually specific overrides global.)
                    // Re-read: GuideScheduleComponent logic implies slot seeds from global, but can be empty? 
                    // Let's assume non-null slot values take precedence. If null, fallback to global. 
                    
                    if (!slotFound)
                    {
                        overrideMeetingTime = sched.MeetingTime;
                        overrideMeetingPlace = sched.MeetingPlace;
                        overrideInstructions = sched.MeetingInstructions;
                        overrideLink = sched.VendorLink;
                    }
                    else
                    {
                         // If slot found, coalesce with global if slot value is missing?
                         if (string.IsNullOrWhiteSpace(overrideMeetingTime)) overrideMeetingTime = sched.MeetingTime;
                         if (string.IsNullOrWhiteSpace(overrideMeetingPlace)) overrideMeetingPlace = sched.MeetingPlace;
                         if (string.IsNullOrWhiteSpace(overrideInstructions)) overrideInstructions = sched.MeetingInstructions;
                         if (string.IsNullOrWhiteSpace(overrideLink)) overrideLink = sched.VendorLink;
                    }

                    // Apply to walker
                    if (!string.IsNullOrWhiteSpace(overrideMeetingPlace)) walker.MeetingPlace = overrideMeetingPlace;
                    if (!string.IsNullOrWhiteSpace(overrideMeetingTime))
                    {
                         walker.MeetingTime = overrideMeetingTime;
                         // Also update MeetingTime display? walker.MeetingTime is just the string.
                    }
                    if (!string.IsNullOrWhiteSpace(overrideInstructions)) walker.MeetingInstructions = overrideInstructions;
                    // Walker data doesn't have VendorLink currently? 
                    // ShapedWalkerData definition check: it doesn't seem to have VendorLink property visible in previous `ViewFile` output (Line 422+).
                    // Checking ShapedWalkerData definition...
                    // Wait, lines 440-441 show MeetingPlace and MeetingTime. MeetingInstructions is empty string at 442.
                    // It does NOT show VendorLink.
                }
            }

            diagnostics.WriteSummary(activeSchedules.Count);
        }

        private List<TourGuideAssignment> DeriveAssignmentsFromSchedules(List<ScheduleMatchInfo> schedules, DateTime? startDate, DateTime? endDate)
        {
            var results = new List<TourGuideAssignment>();
            if (schedules == null || schedules.Count == 0) return results;

            // Define scan range. If filters are null, this could be huge, but usually filters are present.
            // If strictly today+tomorrow, range is small.
            // If startDate/endDate provided, use them.
            // If not, maybe use today -> +3 months? 
            // Better: Iterate schedules and only yield assignments for their valid range that OVERLAP with request.
            
            var effectiveStart = startDate ?? DateTime.Today;
            var effectiveEnd = endDate ?? DateTime.Today.AddDays(30);

            foreach (var match in schedules)
            {
                var s = match.Schedule;
                if (string.IsNullOrWhiteSpace(s.DayAssignmentsJson)) continue;

                // Parse assignments: Key=DayName, Value=GuideIdStr
                Dictionary<string, string>? dayMap = null;
                try
                {
                    dayMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s.DayAssignmentsJson);
                }
                catch { }

                if (dayMap == null || dayMap.Count == 0) continue;

                // Determine overlap between request range and schedule range
                var rangeStart = s.StartDate > effectiveStart ? s.StartDate : effectiveStart;
                var rangeEnd = s.EndDate < effectiveEnd ? s.EndDate : effectiveEnd;

                if (rangeStart > rangeEnd) continue;

                for (var dt = rangeStart.Date; dt <= rangeEnd.Date; dt = dt.AddDays(1))
                {
                    var dayName = dt.DayOfWeek.ToString();
                    if (dayMap.TryGetValue(dayName, out var guideIdStr) && !string.IsNullOrWhiteSpace(guideIdStr))
                    {
                        if (int.TryParse(guideIdStr, out int guideId))
                        {
                            results.Add(new TourGuideAssignment
                            {
                                TourDate = dt,
                                TourName = match.TourName, // Normalized or raw?
                                TourTime = s.MeetingTime, // Use MeetingTime as proxy for TourTime? Or leave fuzzy?
                                // Actually MatchAssignment uses date + normalized name. Time is secondary.
                                // If we don't have exact tour time, we can leave it empty or match all slots?
                                // Schedule applies to ALL slots of that tour usually unless separate schedule?
                                // In DB schema, Schedule is per TourId.
                                // So this assignment applies to this TourName on this Date.
                                GuideId = guideId
                            });
                        }
                    }
                }
            }
            return results;
        }
    }
}
