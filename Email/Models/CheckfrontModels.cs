using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.Json;

namespace Email.Models
{
    /// <summary>
    /// Created: 2026-03-26 00:00 UTC
    /// </summary>
    public sealed class FlexibleStringJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.TryGetInt64(out var longValue)
                    ? longValue.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.True => bool.TrueString.ToLowerInvariant(),
                JsonTokenType.False => bool.FalseString.ToLowerInvariant(),
                JsonTokenType.Null => string.Empty,
                _ => ReadComplexValue(ref reader)
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }

        private static string ReadComplexValue(ref Utf8JsonReader reader)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return doc.RootElement.ToString();
        }
    }

    /// <summary>
    /// Created: 2026-03-26 00:00 UTC
    /// </summary>
    public sealed class SafeIntJsonConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n))
            {
                return n;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            }

            return 0;
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Created: 2026-03-26 00:00 UTC
    /// </summary>
    public sealed class SafeLongJsonConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var n))
            {
                return n;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            }

            return 0;
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    // Configuration model for Checkfront settings
    // Created: 2026-03-26 23:07 UTC
    public class CheckfrontConfig
    {
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ProductionApiEndpoint { get; set; } = string.Empty;
        public string DevelopmentApiEndpoint { get; set; } = string.Empty;
        public string DefaultEndpointMode { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public CheckfrontV4Config V4 { get; set; } = new();
        public string CompanyName { get; set; } = string.Empty;
        public CheckfrontOAuth2Config OAuth2 { get; set; } = new();
    }

    // Configuration model for Checkfront API v4 token settings.
    // Created: 2026-04-01 00:00 UTC
    public class CheckfrontV4Config
    {
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }

    // OAuth2 configuration model for Checkfront API v3.
    // Created: 2026-03-29 00:00 UTC
    public class CheckfrontOAuth2Config
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string CallbackUrl { get; set; } = string.Empty;
        public string AuthorizeUrl { get; set; } = string.Empty;
        public string TokenUrl { get; set; } = string.Empty;
        public string ConsumerKey { get; set; } = string.Empty;
        public string ConsumerSecret { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string AccessTokenExpiresAtUtc { get; set; } = string.Empty;
    }

    // Base response model for Checkfront API
    public class CheckfrontResponse<T>
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("account_id")]
        public int AccountId { get; set; }

        [JsonPropertyName("host_id")]
        public string HostId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("request")]
        public CheckfrontRequestInfo? Request { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    // Company-specific response model
    public class CheckfrontCompanyResponse
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("account_id")]
        public int AccountId { get; set; }

        [JsonPropertyName("host_id")]
        public string HostId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("locale")]
        public CheckfrontLocale? Locale { get; set; }

        [JsonPropertyName("request")]
        public CheckfrontRequestInfo? Request { get; set; }

        [JsonPropertyName("company")]
        public CheckfrontCompany? Company { get; set; }
    }

    // Request info model
    public class CheckfrontRequestInfo
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("resource")]
        public string Resource { get; set; } = string.Empty;

        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;
    }

    // Locale model
    public class CheckfrontLocale
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("lang")]
        public string Lang { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;
    }

    // Company information model
    public class CheckfrontCompany
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("plan_id")]
        public int PlanId { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("city")]
        public string City { get; set; } = string.Empty;

        [JsonPropertyName("postal_zip")]
        public string PostalZip { get; set; } = string.Empty;

        [JsonPropertyName("region")]
        public string Region { get; set; } = string.Empty;

        [JsonPropertyName("country_id")]
        public string CountryId { get; set; } = string.Empty;

        [JsonPropertyName("currency_id")]
        public string CurrencyId { get; set; } = string.Empty;

        [JsonPropertyName("timezone")]
        public string Timezone { get; set; } = string.Empty;

        [JsonPropertyName("locale_id")]
        public string LocaleId { get; set; } = string.Empty;

        [JsonPropertyName("lang_id")]
        public string LangId { get; set; } = string.Empty;

        [JsonPropertyName("date_format")]
        public string DateFormat { get; set; } = string.Empty;

        [JsonPropertyName("time_format")]
        public string TimeFormat { get; set; } = string.Empty;

        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;

        [JsonPropertyName("utc_offset")]
        public int UtcOffset { get; set; }

        [JsonPropertyName("currency")]
        public CheckfrontCurrency? Currency { get; set; }
    }

    // Currency model
    public class CheckfrontCurrency
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("symbol_space")]
        public int SymbolSpace { get; set; }

        [JsonPropertyName("symbol_precedes")]
        public int SymbolPrecedes { get; set; }

        [JsonPropertyName("decimals")]
        public int Decimals { get; set; }

        [JsonPropertyName("decimal_separator")]
        public string DecimalSeparator { get; set; } = string.Empty;

        [JsonPropertyName("thousands_separator")]
        public string ThousandsSeparator { get; set; } = string.Empty;
    }

    // Booking model for API responses
    public class CheckfrontBooking
    {
        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("booking_id")]
        public int BookingId { get; set; }

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("id")]
        public string BookingReference { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("customer")]
        public CheckfrontCustomer? Customer { get; set; }

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("status_id")]
        public string StatusId { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("status_name")]
        public string StatusName { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeLongJsonConverter))]
        [JsonPropertyName("created_date")]
        public long CreatedDateTimestamp { get; set; }

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("total")]
        public string Total { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("tax_total")]
        public string TaxTotal { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("paid_total")]
        public string PaidTotal { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("customer_email")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("customer_phone")]
        public string CustomerPhone { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("date_desc")]
        public string DateDescription { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("start_date")]
        public string StartDateRaw { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("start_time")]
        public string StartTimeRaw { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("date_time")]
        public string DateTimeDescription { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("item_name")]
        public string ItemName { get; set; } = string.Empty;

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("item_title")]
        public string ItemTitle { get; set; } = string.Empty;

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("qty")]
        public int Quantity { get; set; }

        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("total_pax")]
        public int TotalPax { get; set; }

        [JsonIgnore]
        public int NumberOfAttendees { get; set; }

        [JsonIgnore]
        public int NumberOfAdults { get; set; }

        [JsonIgnore]
        public int NumberOfChildren { get; set; }

        [JsonIgnore]
        public List<CheckfrontBookingItem> Items { get; set; } = new();

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("tid")]
        public string? TrackingId { get; set; }

        [JsonConverter(typeof(FlexibleStringJsonConverter))]
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        // Pipeline compatibility property
        [JsonIgnore]
        public string Id => BookingId.ToString();

        // Computed property for created date
        [JsonIgnore]
        public DateTime CreatedDate => DateTimeOffset.FromUnixTimeSeconds(CreatedDateTimestamp).DateTime;
    }

    public class CheckfrontBookingItem
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string StartDateRaw { get; set; } = string.Empty;
        public string StartTimeRaw { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }
        public int TotalPax { get; set; }
    }

    // Customer model
    public class CheckfrontCustomer
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("customer_email")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customer_phone")]
        public string CustomerPhone { get; set; } = string.Empty;

        [JsonPropertyName("customer_address")]
        public string CustomerAddress { get; set; } = string.Empty;
    }

    // Order model
    public class CheckfrontOrder
    {
        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonPropertyName("paid_total")]
        public decimal PaidTotal { get; set; }

        [JsonPropertyName("tax_total")]
        public decimal TaxTotal { get; set; }

        [JsonPropertyName("items")]
        public List<CheckfrontOrderItem> Items { get; set; } = new();
    }

    // Order item model
    public class CheckfrontOrderItem
    {
        [JsonPropertyName("sku")]
        public string Sku { get; set; } = string.Empty;

        [JsonPropertyName("start_date")]
        public DateTime StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public DateTime EndDate { get; set; }

        [JsonPropertyName("total")]
        public decimal Total { get; set; }

        [JsonPropertyName("qty")]
        public int Quantity { get; set; }
    }

    // Item model for inventory
    public class CheckfrontItem
    {
        [JsonPropertyName("item_id")]
        public int ItemId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("sku")]
        public string Sku { get; set; } = string.Empty;

        [JsonPropertyName("category_id")]
        public int CategoryId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("stock")]
        public int Stock { get; set; }
    }

    // Test connection result model
    public class CheckfrontConnectionTest
    {
        public bool IsConnected { get; set; }
        public string Message { get; set; } = string.Empty;
        public CheckfrontCompany? CompanyInfo { get; set; }
        public DateTime TestTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Customer information for search results
    /// </summary>
    public class CheckfrontCustomerInfo
    {
        public string Code { get; set; } = string.Empty;
        
        [JsonConverter(typeof(SafeIntJsonConverter))]
        [JsonPropertyName("customer_id")]
        public int CustomerId { get; set; }

        [JsonPropertyName("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("customer_email")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customer_phone")]
        public string CustomerPhone { get; set; } = string.Empty;

        [JsonPropertyName("customer_first_name")]
        public string CustomerFirstName { get; set; } = string.Empty;

        [JsonPropertyName("customer_last_name")]
        public string CustomerLastName { get; set; } = string.Empty;

        [JsonPropertyName("customer_address")]
        public string CustomerAddress { get; set; } = string.Empty;

        /// <summary>
        /// Created: 2026-03-26 00:00 UTC
        /// Raw customer identifier from Checkfront payload (supports non-numeric ids like TH1-878-188).
        /// </summary>
        [JsonIgnore]
        public string CustomerIdRaw { get; set; } = string.Empty;

        // Pipeline compatibility properties
        public string Id => CustomerId.ToString();
        public string Name => CustomerName;
        public string Email => CustomerEmail;
        public string Phone => CustomerPhone;
    }

    /// <summary>
    /// Customer match result with confidence scoring
    /// </summary>
    public class CheckfrontCustomerMatch
    {
        public CheckfrontCustomerInfo Customer { get; set; } = new();
        public string MatchType { get; set; } = string.Empty; // phone, email, name
        public decimal ConfidenceScore { get; set; } // 0-100
        public string MatchReason { get; set; } = string.Empty;
        
        // Pipeline compatibility properties
        public string MatchStrategy => MatchType;
        public string SearchTerm { get; set; } = string.Empty;
    }
}
