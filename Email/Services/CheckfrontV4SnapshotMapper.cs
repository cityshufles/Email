using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Email.Models;

namespace Email.Services
{
    /// <summary>
    /// Created: 2026-04-04 00:00 UTC
    /// Maps dbo.CheckfrontV4Bookings snapshot rows into legacy Checkfront booking shape
    /// so existing GmailProcessingV2 merge logic can be reused without broad refactors.
    /// </summary>
    public sealed class CheckfrontV4SnapshotMapper
    {
        public bool TryMapToLegacyBooking(
            DbCheckfrontV4Booking snapshot,
            out CheckfrontBooking booking,
            out string reason)
        {
            booking = new CheckfrontBooking();
            reason = string.Empty;

            if (snapshot == null)
            {
                reason = "Snapshot row is null.";
                return false;
            }

            var parsed = TryDeserializeSnapshot(snapshot.SnapshotJson);
            var parsedCustomer = parsed?.Customer ?? TryDeserializeCustomer(snapshot.CustomerJson);
            var fields = MergeFields(parsed?.Fields, ParseFieldsJson(snapshot.FieldsJson), parsedCustomer?.Fields);
            var bookingCode = FirstNonEmpty(parsed?.Code, snapshot.BookingCode, parsed?.Id, snapshot.CheckfrontBookingId);
            if (string.IsNullOrWhiteSpace(bookingCode))
            {
                reason = "Missing booking code.";
                return false;
            }

            var tourName = FirstNonEmpty(
                parsed?.ItemSummary,
                snapshot.ItemSummary,
                ResolveTourNameFromFields(fields),
                ResolveTourNameFromAdditionalProperties(parsed?.AdditionalProperties));
            if (string.IsNullOrWhiteSpace(tourName))
            {
                reason = "Missing tour name/item summary.";
                return false;
            }

            var startAt = FirstNonNull(
                ParseDateTimeOffset(parsed?.Start),
                snapshot.StartAtLocal,
                ParseDateTimeOffset(parsed?.CheckIn),
                snapshot.CheckInAtLocal,
                ParseDateTimeOffset(ExtractFieldString(fields, "start")),
                ParseDateTimeOffset(ExtractFieldString(fields, "check_in")));
            if (!startAt.HasValue)
            {
                reason = "Missing start/check-in date-time.";
                return false;
            }

            var customerPhone = FirstNonEmpty(
                ExtractFieldString(fields, "customer_phone"),
                snapshot.CustomerPhone);

            var adults = ResolveAttendeeValue(
                fields,
                new[]
                {
                    "adults",
                    "adult",
                    "number_of_adults",
                    "numberofadults",
                    "adults_count",
                    "pax_adults",
                    "total_adults"
                });
            if (adults <= 0)
            {
                adults = ResolveAttendeeValue(
                    parsed?.AdditionalProperties,
                    new[]
                    {
                        "adults",
                        "adult",
                        "numberOfAdults",
                        "number_of_adults",
                        "paxAdults"
                    });
            }

            var children = ResolveAttendeeValue(
                fields,
                new[]
                {
                    "children",
                    "child",
                    "number_of_children",
                    "numberofchildren",
                    "children_count",
                    "pax_children",
                    "total_children"
                });
            if (children <= 0)
            {
                children = ResolveAttendeeValue(
                    parsed?.AdditionalProperties,
                    new[]
                    {
                        "children",
                        "child",
                        "numberOfChildren",
                        "number_of_children",
                        "paxChildren"
                    });
            }

            var attendees = ResolveAttendeeValue(
                fields,
                new[]
                {
                    "attendees",
                    "attendee",
                    "participants",
                    "guests",
                    "pax",
                    "total_pax",
                    "total_attendees",
                    "number_of_attendees",
                    "numberofattendees",
                    "qty",
                    "quantity"
                });
            if (attendees <= 0)
            {
                attendees = ResolveAttendeeValue(
                    parsed?.AdditionalProperties,
                    new[]
                    {
                        "totalPax",
                        "total_pax",
                        "numberOfAttendees",
                        "number_of_attendees",
                        "attendees",
                        "qty",
                        "quantity"
                    });
            }

            if (attendees <= 0 && (adults > 0 || children > 0))
            {
                attendees = adults + children;
            }

            if (attendees <= 0)
            {
                attendees = TryParseAttendeeCount(tourName);
            }

            var createdAt = FirstNonNull(ParseDateTimeOffset(parsed?.Created), snapshot.CreatedAtLocal);
            var customerFirstName = FirstNonEmpty(
                parsed?.FirstName,
                snapshot.CustomerFirstName,
                parsedCustomer?.FirstName,
                ExtractFieldString(fields, "customer_first_name"),
                ExtractFieldString(fields, "first_name"));
            var customerLastName = FirstNonEmpty(
                parsed?.LastName,
                snapshot.CustomerLastName,
                parsedCustomer?.LastName,
                ExtractFieldString(fields, "customer_last_name"),
                ExtractFieldString(fields, "last_name"));
            var explicitCustomerName = FirstNonEmpty(
                ExtractFieldString(fields, "customer_name"),
                ExtractFieldString(fields, "name"));
            var customerName = BuildCustomerName(customerFirstName, customerLastName, explicitCustomerName);
            var customerEmail = FirstNonEmpty(
                parsed?.Email,
                snapshot.CustomerEmail,
                parsedCustomer?.Email,
                ExtractFieldString(fields, "customer_email"));

            booking = new CheckfrontBooking
            {
                BookingId = TryParseInt(parsed?.Id) ?? TryParseInt(snapshot.CheckfrontBookingId) ?? 0,
                Code = bookingCode,
                BookingReference = FirstNonEmpty(parsed?.Id, snapshot.CheckfrontBookingId),
                Status = FirstNonEmpty(parsed?.Status?.Name, snapshot.StatusName, parsed?.StatusId, snapshot.StatusId),
                StatusId = FirstNonEmpty(parsed?.StatusId, parsed?.Status?.Id, snapshot.StatusId),
                StatusName = FirstNonEmpty(parsed?.Status?.Name, snapshot.StatusName),
                CreatedDateTimestamp = createdAt.HasValue
                    ? new DateTimeOffset(createdAt.Value.UtcDateTime).ToUnixTimeSeconds()
                    : 0,
                Total = parsed != null ? parsed.Total.ToString(CultureInfo.InvariantCulture) : (snapshot.Total?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                TaxTotal = parsed != null ? parsed.TaxTotal.ToString(CultureInfo.InvariantCulture) : (snapshot.TaxTotal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                PaidTotal = parsed != null ? parsed.PaidTotal.ToString(CultureInfo.InvariantCulture) : (snapshot.PaidTotal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                CustomerName = customerName,
                CustomerEmail = customerEmail,
                CustomerPhone = customerPhone,
                CustomerId = parsed?.CustomerId > 0 ? parsed.CustomerId : (snapshot.CustomerId ?? 0),
                Summary = tourName,
                ItemName = tourName,
                ItemTitle = tourName,
                DateDescription = startAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                StartDateRaw = startAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StartTimeRaw = startAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
                DateTimeDescription = startAt.Value.ToString("O", CultureInfo.InvariantCulture),
                Quantity = attendees > 0 ? attendees : 0,
                TotalPax = attendees > 0 ? attendees : 0,
                NumberOfAdults = adults > 0 ? adults : 0,
                NumberOfChildren = children > 0 ? children : 0,
                NumberOfAttendees = attendees > 0 ? attendees : 0,
                Customer = new CheckfrontCustomer
                {
                    Code = FirstNonEmpty(parsedCustomer?.Code, snapshot.CustomerCode),
                    CustomerName = customerName,
                    CustomerEmail = FirstNonEmpty(parsedCustomer?.Email, customerEmail),
                    CustomerPhone = customerPhone
                }
            };

            return true;
        }

        private static CheckfrontV4Booking? TryDeserializeSnapshot(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<CheckfrontV4Booking>(json);
            }
            catch
            {
                return null;
            }
        }

        private static CheckfrontV4Customer? TryDeserializeCustomer(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<CheckfrontV4Customer>(json);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, JsonElement> ParseFieldsJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                return parsed == null
                    ? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, JsonElement>(parsed, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static Dictionary<string, JsonElement> MergeFields(
            Dictionary<string, JsonElement>? fromSnapshotJson,
            Dictionary<string, JsonElement> fromColumns,
            Dictionary<string, JsonElement>? fromCustomerJson)
        {
            var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (fromColumns != null)
            {
                foreach (var kv in fromColumns)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            if (fromCustomerJson != null)
            {
                foreach (var kv in fromCustomerJson)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            if (fromSnapshotJson != null)
            {
                foreach (var kv in fromSnapshotJson)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            return merged;
        }

        private static string ExtractFieldString(Dictionary<string, JsonElement> fields, string fieldName)
        {
            if (!fields.TryGetValue(fieldName, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static string ResolveTourNameFromFields(Dictionary<string, JsonElement> fields)
        {
            return FirstNonEmpty(
                ExtractFieldString(fields, "item_summary"),
                ExtractFieldString(fields, "itemsummary"),
                ExtractFieldString(fields, "item_name"),
                ExtractFieldString(fields, "itemname"),
                ExtractFieldString(fields, "item_title"),
                ExtractFieldString(fields, "itemtitle"),
                ExtractFieldString(fields, "tour_name"),
                ExtractFieldString(fields, "tourname"),
                ExtractFieldString(fields, "tour_title"),
                ExtractFieldString(fields, "tourtitle"),
                ExtractFieldString(fields, "product_name"),
                ExtractFieldString(fields, "productname"),
                ExtractFieldString(fields, "product_title"),
                ExtractFieldString(fields, "producttitle"),
                ExtractFieldString(fields, "summary"),
                ExtractFieldString(fields, "item"),
                ExtractFieldString(fields, "tour"),
                ExtractFieldString(fields, "product"));
        }

        private static string ResolveTourNameFromAdditionalProperties(
            Dictionary<string, JsonElement>? additionalProperties)
        {
            if (additionalProperties == null || additionalProperties.Count == 0)
            {
                return string.Empty;
            }

            var direct = FirstNonEmpty(
                ExtractAdditionalString(additionalProperties, "itemSummary"),
                ExtractAdditionalString(additionalProperties, "item_summary"),
                ExtractAdditionalString(additionalProperties, "itemName"),
                ExtractAdditionalString(additionalProperties, "item_name"),
                ExtractAdditionalString(additionalProperties, "itemTitle"),
                ExtractAdditionalString(additionalProperties, "item_title"),
                ExtractAdditionalString(additionalProperties, "tourName"),
                ExtractAdditionalString(additionalProperties, "tour_name"),
                ExtractAdditionalString(additionalProperties, "productName"),
                ExtractAdditionalString(additionalProperties, "product_name"),
                ExtractAdditionalString(additionalProperties, "summary"),
                ExtractAdditionalString(additionalProperties, "title"),
                ExtractAdditionalString(additionalProperties, "name"));
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            return FirstNonEmpty(
                ResolveTourNameFromAdditionalComplexValue(additionalProperties, "items"),
                ResolveTourNameFromAdditionalComplexValue(additionalProperties, "item"),
                ResolveTourNameFromAdditionalComplexValue(additionalProperties, "tour"),
                ResolveTourNameFromAdditionalComplexValue(additionalProperties, "product"),
                ResolveTourNameFromAdditionalComplexValue(additionalProperties, "activity"),
                ResolveTourNameFromAdditionalComplexValue(additionalProperties, "experience"));
        }

        private static string ExtractAdditionalString(
            Dictionary<string, JsonElement> additionalProperties,
            string key)
        {
            if (!additionalProperties.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => string.Empty
            };
        }

        private static string ResolveTourNameFromAdditionalComplexValue(
            Dictionary<string, JsonElement> additionalProperties,
            string key)
        {
            if (!additionalProperties.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            return ResolveTourNameFromNestedElement(value);
        }

        private static string ResolveTourNameFromNestedElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;

                case JsonValueKind.Array:
                    foreach (var arrayElement in element.EnumerateArray())
                    {
                        var nested = ResolveTourNameFromNestedElement(arrayElement);
                        if (!string.IsNullOrWhiteSpace(nested))
                        {
                            return nested;
                        }
                    }
                    return string.Empty;

                case JsonValueKind.Object:
                    var objectDirect = FirstNonEmpty(
                        TryGetObjectPropertyString(element, "itemSummary"),
                        TryGetObjectPropertyString(element, "item_summary"),
                        TryGetObjectPropertyString(element, "itemName"),
                        TryGetObjectPropertyString(element, "item_name"),
                        TryGetObjectPropertyString(element, "itemTitle"),
                        TryGetObjectPropertyString(element, "item_title"),
                        TryGetObjectPropertyString(element, "tourName"),
                        TryGetObjectPropertyString(element, "tour_name"),
                        TryGetObjectPropertyString(element, "productName"),
                        TryGetObjectPropertyString(element, "product_name"),
                        TryGetObjectPropertyString(element, "summary"),
                        TryGetObjectPropertyString(element, "title"),
                        TryGetObjectPropertyString(element, "name"));
                    if (!string.IsNullOrWhiteSpace(objectDirect))
                    {
                        return objectDirect;
                    }

                    var nestedObject = FirstNonEmpty(
                        TryResolveNestedObjectProperty(element, "item"),
                        TryResolveNestedObjectProperty(element, "items"),
                        TryResolveNestedObjectProperty(element, "tour"),
                        TryResolveNestedObjectProperty(element, "product"),
                        TryResolveNestedObjectProperty(element, "activity"),
                        TryResolveNestedObjectProperty(element, "experience"));
                    if (!string.IsNullOrWhiteSpace(nestedObject))
                    {
                        return nestedObject;
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        var value = property.Value;
                        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        {
                            var nestedValue = ResolveTourNameFromNestedElement(value);
                            if (!string.IsNullOrWhiteSpace(nestedValue))
                            {
                                return nestedValue;
                            }
                        }
                    }

                    return string.Empty;

                default:
                    return string.Empty;
            }
        }

        private static string TryResolveNestedObjectProperty(JsonElement element, string propertyName)
        {
            return TryGetPropertyIgnoreCase(element, propertyName, out var value)
                ? ResolveTourNameFromNestedElement(value)
                : string.Empty;
        }

        private static string TryGetObjectPropertyString(JsonElement element, string propertyName)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => string.Empty
            };
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string propertyName,
            out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static int ResolveAttendeeValue(Dictionary<string, JsonElement> fields, IEnumerable<string> preferredKeys)
        {
            if (fields == null || fields.Count == 0)
            {
                return 0;
            }

            foreach (var key in preferredKeys)
            {
                if (!fields.TryGetValue(key, out var value))
                {
                    continue;
                }

                var parsed = TryParseInt(value);
                if (parsed.HasValue && parsed.Value > 0)
                {
                    return parsed.Value;
                }
            }

            return 0;
        }

        private static int ResolveAttendeeValue(Dictionary<string, JsonElement>? fields, string[] preferredKeys)
        {
            return fields == null
                ? 0
                : ResolveAttendeeValue(fields, (IEnumerable<string>)preferredKeys);
        }

        private static int? TryParseInt(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString))
                {
                    return parsedString;
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var numeric = Regex.Match(raw, @"\d+");
                    if (numeric.Success &&
                        int.TryParse(numeric.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFromText))
                    {
                        return parsedFromText;
                    }
                }
            }

            return null;
        }

        private static int? TryParseInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            var numeric = Regex.Match(value, @"\d+");
            if (numeric.Success &&
                int.TryParse(numeric.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFromText))
            {
                return parsedFromText;
            }

            return null;
        }

        private static DateTimeOffset? ParseDateTimeOffset(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
        }

        private static int TryParseAttendeeCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var labelled = Regex.Match(text, @"(?<value>\d+)\s*(?:adults?|attendees?|guests?|participants?|travelers?|travellers?)", RegexOptions.IgnoreCase);
            if (labelled.Success &&
                int.TryParse(labelled.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var labelledValue))
            {
                return labelledValue;
            }

            var first = Regex.Match(text, @"(?<value>\d+)");
            if (first.Success &&
                int.TryParse(first.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var firstValue))
            {
                return firstValue;
            }

            return 0;
        }

        private static string BuildCustomerName(string firstName, string lastName, string explicitCustomerName)
        {
            var full = string.Join(" ", new[] { firstName, lastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            if (!string.IsNullOrWhiteSpace(full))
            {
                return full;
            }

            if (!string.IsNullOrWhiteSpace(explicitCustomerName))
            {
                return explicitCustomerName.Trim();
            }

            return "Unknown Customer";
        }

        private static DateTimeOffset? FirstNonNull(params DateTimeOffset?[] values)
        {
            return values.FirstOrDefault(x => x.HasValue);
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }
    }
}
