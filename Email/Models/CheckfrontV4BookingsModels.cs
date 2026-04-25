using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Read model for Checkfront API v4 List Bookings response payload.
    /// </summary>
    public sealed class CheckfrontV4BookingsListResponse
    {
        [JsonPropertyName("data")]
        public List<CheckfrontV4Booking> Data { get; set; } = new();
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Booking object shape from Checkfront API v4 bookings list endpoint.
    /// </summary>
    public sealed class CheckfrontV4Booking
    {
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("created")]
        public string Created { get; set; } = string.Empty;

        [JsonPropertyName("start")]
        public string Start { get; set; } = string.Empty;

        [JsonPropertyName("end")]
        public string End { get; set; } = string.Empty;

        [JsonPropertyName("checkIn")]
        public string CheckIn { get; set; } = string.Empty;

        [JsonPropertyName("checkOut")]
        public string CheckOut { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("customerId")]
        public int CustomerId { get; set; }

        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("basePrice")]
        public decimal BasePrice { get; set; }

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("discountTotal")]
        public decimal DiscountTotal { get; set; }

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("subTotal")]
        public decimal SubTotal { get; set; }

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("inclusiveTaxTotal")]
        public decimal InclusiveTaxTotal { get; set; }

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("taxTotal")]
        public decimal TaxTotal { get; set; }

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonConverter(typeof(SafeDecimalJsonConverter))]
        [JsonPropertyName("paidTotal")]
        public decimal PaidTotal { get; set; }

        [JsonPropertyName("statusId")]
        public string StatusId { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("accountId")]
        public int AccountId { get; set; }

        [JsonPropertyName("partnerId")]
        public string? PartnerId { get; set; }

        [JsonPropertyName("itemSummary")]
        public string ItemSummary { get; set; } = string.Empty;

        [JsonPropertyName("cfx")]
        public string Cfx { get; set; } = string.Empty;

        [JsonPropertyName("gcfx")]
        public string Gcfx { get; set; } = string.Empty;

        [JsonPropertyName("fields")]
        public Dictionary<string, JsonElement> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("status")]
        public CheckfrontV4Status? Status { get; set; }

        [JsonPropertyName("customer")]
        public CheckfrontV4Customer? Customer { get; set; }

        [JsonPropertyName("notes")]
        public List<CheckfrontV4BookingNote> Notes { get; set; } = new();

        [JsonPropertyName("discountCode")]
        public string DiscountCode { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeBoolJsonConverter))]
        [JsonPropertyName("isMobile")]
        public bool IsMobile { get; set; }

        [JsonIgnore]
        public string RawJson { get; set; } = string.Empty;

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Booking status object in Checkfront v4 payloads.
    /// </summary>
    public sealed class CheckfrontV4Status
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("position")]
        public int Position { get; set; }

        [JsonConverter(typeof(SafeBoolJsonConverter))]
        [JsonPropertyName("locking")]
        public bool Locking { get; set; }

        [JsonConverter(typeof(SafeBoolJsonConverter))]
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Nested customer object in Checkfront v4 bookings.
    /// </summary>
    public sealed class CheckfrontV4Customer
    {
        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("account")]
        public CheckfrontV4CustomerAccount? Account { get; set; }

        [JsonPropertyName("bookingStats")]
        public List<CheckfrontV4CustomerBookingStats> BookingStats { get; set; } = new();

        [JsonPropertyName("fields")]
        public Dictionary<string, JsonElement> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("newestBooking")]
        public JsonElement? NewestBooking { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Nested account object for customer.
    /// </summary>
    public sealed class CheckfrontV4CustomerAccount
    {
        [JsonPropertyName("dateLastLogin")]
        public string DateLastLogin { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeBoolJsonConverter))]
        [JsonPropertyName("emailVerified")]
        public bool EmailVerified { get; set; }

        [JsonConverter(typeof(SafeBoolJsonConverter))]
        [JsonPropertyName("claimed")]
        public bool Claimed { get; set; }
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Nested booking stats object for customer.
    /// </summary>
    public sealed class CheckfrontV4CustomerBookingStats
    {
        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("numberOfBookings")]
        public int NumberOfBookings { get; set; }

        [JsonPropertyName("sumOfBookingTotals")]
        public string SumOfBookingTotals { get; set; } = string.Empty;
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Nested booking note object.
    /// </summary>
    public sealed class CheckfrontV4BookingNote
    {
        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("bookingId")]
        public int BookingId { get; set; }

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("accountId")]
        public int AccountId { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeBoolJsonConverter))]
        [JsonPropertyName("public")]
        public bool IsPublic { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Converter for decimal fields that may be emitted as numbers, strings, or null.
    /// </summary>
    public sealed class SafeDecimalJsonConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number))
            {
                return number;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0m;
            }

            return 0m;
        }

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Created: 2026-04-03 00:00 UTC
    /// Converter for bool fields that may be emitted as bool/number/string.
    /// </summary>
    public sealed class SafeBoolJsonConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True)
            {
                return true;
            }

            if (reader.TokenType == JsonTokenType.False)
            {
                return false;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return number != 0;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (bool.TryParse(value, out var parsedBool))
                {
                    return parsedBool;
                }

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                {
                    return parsedInt != 0;
                }
            }

            return false;
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
    }
}
