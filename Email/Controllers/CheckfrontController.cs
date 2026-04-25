using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WhatsAppBusinessAPI.Hubs;
using Email.Models;
using Email.Services;

namespace WhatsAppBusinessAPI.Controllers
{
    /// <summary>
    /// Checkfront booking system integration and synchronization
    /// </summary>
    /// <remarks>
    /// This controller manages all interactions with the Checkfront booking platform including:
    /// 
    /// **Booking Management:**
    /// - **Booking Sync**: Synchronize tour bookings between systems
    /// - **Customer Integration**: Match and sync customer data with Checkfront
    /// - **Real-time Updates**: Handle booking changes and cancellations
    /// - **Inventory Management**: Track tour availability and capacity
    /// 
    /// **Data Synchronization:**
    /// - **Two-way Sync**: Keep booking data consistent across platforms
    /// - **Conflict Resolution**: Handle data conflicts and duplicates
    /// - **Batch Operations**: Efficient bulk data synchronization
    /// - **Error Recovery**: Robust error handling and retry mechanisms
    /// 
    /// **Webhook Processing:**
    /// - **Real-time Events**: Process Checkfront webhook notifications
    /// - **Event Logging**: Complete audit trail of all webhook events
    /// - **Automated Responses**: Trigger actions based on booking events
    /// - **Status Monitoring**: Track webhook delivery and processing status
    /// 
    /// **API Integration:**
    /// - **Connection Testing**: Verify Checkfront API connectivity
    /// - **Authentication**: Manage API credentials and tokens
    /// - **Rate Limiting**: Handle API rate limits and throttling
    /// - **Error Handling**: Comprehensive error logging and recovery
    /// 
    /// Perfect for:
    /// - **Operations Teams**: Monitor booking synchronization status
    /// - **IT Teams**: Manage API integrations and troubleshoot issues
    /// - **Customer Service**: Verify booking status across systems
    /// - **Business Analytics**: Analyze booking patterns and system performance
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    public class CheckfrontController : ControllerBase
    {
        private readonly CheckfrontService _checkfrontService;
        private readonly ILogger<CheckfrontController> _logger;
        private readonly IHubContext<CheckfrontHub> _hubContext;

        private static List<WebhookLogInfo> _webhookLogs = new();
        private static readonly object _webhookLogsLock = new();

        public CheckfrontController(
            CheckfrontService checkfrontService, 
            ILogger<CheckfrontController> logger,
            IHubContext<CheckfrontHub> hubContext)
        {
            _checkfrontService = checkfrontService;
            _logger = logger;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Test connection to Checkfront API and validate configuration
        /// </summary>
        /// <remarks>
        /// Performs comprehensive testing of the Checkfront API connection including:
        /// 
        /// **Connection Validation:**
        /// - **API Connectivity**: Verify Checkfront API is accessible
        /// - **Authentication**: Validate API credentials and authentication tokens
        /// - **Permissions**: Check required API permissions and access levels
        /// - **Configuration**: Verify all required configuration parameters
        /// 
        /// **Functionality Testing:**
        /// - **Company Access**: Test access to company information endpoint
        /// - **Data Retrieval**: Verify ability to retrieve booking and item data
        /// - **API Limits**: Check API rate limits and quota availability
        /// - **Response Validation**: Ensure API responses are properly formatted
        /// 
        /// **Error Detection:**
        /// - **Network Issues**: Detect connectivity and network problems
        /// - **Authentication Failures**: Identify credential or permission issues
        /// - **API Errors**: Capture and report API-specific error conditions
        /// - **Configuration Problems**: Identify setup and configuration issues
        /// 
        /// Use this endpoint to:
        /// - **Deployment Validation**: Verify configuration after deployment
        /// - **Troubleshooting**: Diagnose Checkfront integration issues
        /// - **Health Monitoring**: Regular health checks of the integration
        /// - **Setup Verification**: Confirm setup during initial configuration
        /// </remarks>
        /// <returns>Comprehensive connection test result with detailed status information</returns>
        /// <response code="200">Connection successful with test details</response>
        /// <response code="400">Connection failed with error details</response>
        /// <response code="500">Internal server error during connection test</response>
        [HttpGet("test-connection")]
        public async Task<ActionResult<CheckfrontConnectionTest>> TestConnection()
        {
            try
            {
                var result = await _checkfrontService.TestConnectionAsync();
                
                if (result.IsConnected)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test-connection endpoint");
                return StatusCode(500, new CheckfrontConnectionTest
                {
                    IsConnected = false,
                    Message = $"Internal server error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Retrieve company information from Checkfront
        /// </summary>
        /// <remarks>
        /// Fetches comprehensive company information from the Checkfront system including:
        /// 
        /// **Company Details:**
        /// - **Basic Information**: Company name, address, and contact details
        /// - **Business Settings**: Operating hours, time zones, and localization
        /// - **Branding Information**: Company logo, colors, and branding elements
        /// - **Configuration**: System settings and feature configurations
        /// 
        /// **Integration Context:**
        /// - **System Validation**: Verify connection to correct Checkfront account
        /// - **Configuration Sync**: Ensure local settings match Checkfront configuration
        /// - **Branding Consistency**: Maintain consistent branding across systems
        /// - **Business Logic**: Apply company-specific business rules and settings
        /// 
        /// **Use Cases:**
        /// - **System Setup**: Initial configuration and validation
        /// - **Dashboard Display**: Show company information in admin interfaces
        /// - **Branding**: Apply company branding to customer communications
        /// - **Troubleshooting**: Verify correct account and configuration
        /// </remarks>
        /// <returns>Complete company information from Checkfront</returns>
        /// <response code="200">Company information retrieved successfully</response>
        /// <response code="404">Company information not found</response>
        /// <response code="500">Error retrieving company information</response>
        [HttpGet("company")]
        public async Task<ActionResult<CheckfrontCompany>> GetCompany()
        {
            try
            {
                var company = await _checkfrontService.GetCompanyInfoAsync();
                
                if (company != null)
                {
                    return Ok(company);
                }
                else
                {
                    return NotFound("Company information not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting company information");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieve tour items and inventory from Checkfront
        /// </summary>
        /// <remarks>
        /// Fetches all available tour items and inventory information from Checkfront including:
        /// 
        /// **Item Information:**
        /// - **Tour Details**: Tour names, descriptions, and categories
        /// - **Pricing**: Current pricing, discounts, and special offers
        /// - **Availability**: Real-time availability and capacity information
        /// - **Scheduling**: Tour schedules, time slots, and duration
        /// 
        /// **Inventory Management:**
        /// - **Capacity Tracking**: Monitor tour capacity and bookings
        /// - **Season Management**: Handle seasonal availability and pricing
        /// - **Resource Allocation**: Track guide assignments and equipment
        /// - **Dynamic Pricing**: Support for dynamic pricing and promotions
        /// 
        /// **Integration Support:**
        /// - **Synchronization**: Keep local tour data synchronized with Checkfront
        /// - **Booking Validation**: Validate tour availability before booking
        /// - **Price Updates**: Maintain current pricing information
        /// - **Catalog Management**: Manage tour catalog and offerings
        /// 
        /// **Use Cases:**
        /// - **Tour Management**: Maintain comprehensive tour catalog
        /// - **Booking System**: Support real-time booking availability
        /// - **Price Management**: Keep pricing information current
        /// - **Inventory Control**: Monitor and manage tour capacity
        /// </remarks>
        /// <returns>List of all tour items and inventory from Checkfront</returns>
        /// <response code="200">Items retrieved successfully</response>
        /// <response code="500">Error retrieving items from Checkfront</response>
        [HttpGet("items")]
        public async Task<ActionResult<List<CheckfrontItem>>> GetItems()
        {
            try
            {
                var items = await _checkfrontService.GetItemsAsync();
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting items");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieve bookings from Checkfront with flexible filtering options
        /// </summary>
        /// <remarks>
        /// Fetches booking data from Checkfront with comprehensive filtering and pagination support.
        /// This endpoint provides access to all booking information including:
        /// 
        /// **Booking Information:**
        /// - **Customer Details**: Complete customer information and contact data
        /// - **Tour Details**: Tour information, dates, times, and participant counts
        /// - **Payment Status**: Payment information, amounts, and transaction details
        /// - **Booking Status**: Current status, confirmations, and modifications
        /// 
        /// **Filtering Options:**
        /// - **Date Range**: Filter bookings by date range (start and end dates)
        /// - **Status Filtering**: Filter by booking status (confirmed, pending, cancelled)
        /// - **Pagination**: Control result size with limit and page parameters
        /// - **Advanced Filters**: Additional filtering options for specific use cases
        /// 
        /// **Data Management:**
        /// - **Real-time Data**: Access to current booking information
        /// - **Historical Data**: Retrieve historical booking records
        /// - **Bulk Operations**: Efficient retrieval of large booking datasets
        /// - **Export Support**: Data suitable for export and reporting
        /// 
        /// **Use Cases:**
        /// - **Operations Management**: Monitor daily booking operations
        /// - **Customer Service**: Look up specific customer bookings
        /// - **Reporting**: Generate booking reports and analytics
        /// - **Synchronization**: Keep local booking data synchronized
        /// </remarks>
        /// <param name="startDate">Filter bookings from this date onwards</param>
        /// <param name="endDate">Filter bookings up to this date</param>
        /// <param name="status">Filter bookings by status (confirmed, pending, cancelled)</param>
        /// <param name="limit">Maximum number of bookings to return</param>
        /// <param name="page">Page number for pagination</param>
        /// <returns>Filtered list of bookings with comprehensive booking information</returns>
        /// <response code="200">Bookings retrieved successfully</response>
        /// <response code="500">Error retrieving bookings from Checkfront</response>
        [HttpGet("booking")]
        public async Task<IActionResult> GetBookings(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] string? status = null,
            [FromQuery] int? limit = null,
            [FromQuery] int? page = null)
        {
            try
            {
                var bookings = await _checkfrontService.GetBookingsAsync(startDate, endDate, status, limit, page);
                return Ok(new { success = true, data = bookings, count = bookings.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting bookings");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get webhook logs with pagination and filtering
        /// </summary>
        [HttpGet("webhook-logs")]
        public ActionResult<WebhookLogResponse> GetWebhookLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? eventType = null,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                lock (_webhookLogsLock)
                {
                    var query = _webhookLogs.AsQueryable();

                    if (!string.IsNullOrEmpty(eventType))
                        query = query.Where(l => l.EventType == eventType);

                    if (!string.IsNullOrEmpty(status))
                        query = query.Where(l => l.ProcessingStatus == status);

                    if (startDate.HasValue)
                        query = query.Where(l => l.Timestamp >= startDate.Value);

                    if (endDate.HasValue)
                        query = query.Where(l => l.Timestamp <= endDate.Value);

                    var totalCount = query.Count();
                    var logs = query
                        .OrderByDescending(l => l.Timestamp)
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    return Ok(new WebhookLogResponse
                    {
                        Logs = logs,
                        TotalCount = totalCount,
                        Page = page,
                        PageSize = pageSize,
                        HasMore = (page * pageSize) < totalCount
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving webhook logs");
                return StatusCode(500, "Error retrieving webhook logs");
            }
        }

        /// <summary>
        /// Clear webhook logs older than specified days
        /// </summary>
        [HttpDelete("webhook-logs")]
        public ActionResult ClearWebhookLogs([FromQuery] int olderThanDays = 30)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
                lock (_webhookLogsLock)
                {
                    _webhookLogs.RemoveAll(l => l.Timestamp < cutoffDate);
                }
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing webhook logs");
                return StatusCode(500, "Error clearing webhook logs");
            }
        }

        /// <summary>
        /// Get webhook statistics
        /// </summary>
        [HttpGet("webhook-stats")]
        public ActionResult<WebhookStats> GetWebhookStats([FromQuery] int days = 7)
        {
            try
            {
                var startDate = DateTime.UtcNow.AddDays(-days);
                lock (_webhookLogsLock)
                {
                    var recentLogs = _webhookLogs.Where(l => l.Timestamp >= startDate).ToList();
                    var stats = new WebhookStats
                    {
                        TotalWebhooks = recentLogs.Count,
                        SuccessfulWebhooks = recentLogs.Count(l => l.ProcessingStatus == "Success"),
                        FailedWebhooks = recentLogs.Count(l => l.ProcessingStatus == "Failed"),
                        AverageProcessingTime = recentLogs.Any() ? (int)recentLogs.Average(l => l.ProcessingTimeMs) : 0,
                        RecentActivity = recentLogs.OrderByDescending(l => l.Timestamp).Take(10).ToList()
                    };

                    stats.SuccessRate = stats.TotalWebhooks > 0 
                        ? (double)stats.SuccessfulWebhooks / stats.TotalWebhooks * 100 
                        : 0;

                    stats.EventTypes = recentLogs
                        .GroupBy(l => l.EventType)
                        .Select(g => new WebhookEventTypeStats
                        {
                            EventType = g.Key,
                            Count = g.Count()
                        })
                        .ToList();

                    return Ok(stats);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting webhook stats");
                return StatusCode(500, "Error getting webhook stats");
            }
        }

        /// <summary>
        /// Webhook endpoint for Checkfront notifications
        /// This is where Checkfront will send booking notifications
        /// </summary>
        [HttpPost("webhook")]
        public async Task<IActionResult> HandleWebhook()
        {
            var startTime = DateTime.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogInformation("=== CHECKFRONT WEBHOOK RECEIVED ===");
            _logger.LogInformation("Webhook endpoint hit at {Time}", startTime);
            _logger.LogInformation("Request IP: {IP}", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            _logger.LogInformation("User Agent: {UserAgent}", Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown");
            
            // Initialize webhook log entry
            var webhookLog = new WebhookLogInfo
            {
                Id = _webhookLogs.Count + 1,
                Timestamp = startTime,
                HttpMethod = Request.Method,
                Endpoint = Request.Path,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown",
                ProcessingStatus = "Processing"
            };
            
            try
            {
                // Read the raw body
                using var reader = new StreamReader(Request.Body);
                var payload = await reader.ReadToEndAsync();
                
                webhookLog.RawPayload = payload;

                // Log all headers for debugging
                var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
                webhookLog.Headers = JsonSerializer.Serialize(headers);
                
                _logger.LogInformation("Received Checkfront webhook with headers: {Headers}", JsonSerializer.Serialize(headers));
                _logger.LogInformation("Received Checkfront webhook payload: {Payload}", payload);

                // Get headers for verification if needed
                var signature = Request.Headers["X-Checkfront-Signature"].FirstOrDefault() ?? "";
                var eventType = Request.Headers["X-Checkfront-Event"].FirstOrDefault() ?? "unknown";
                
                webhookLog.Signature = signature;
                webhookLog.EventType = eventType;

                _logger.LogInformation("Webhook event type: {EventType}, Signature: {Signature}", eventType, signature);

                // Try to parse the webhook payload
                try
                {
                    var webhookData = JsonSerializer.Deserialize<CheckfrontWebhookPayload>(payload);
                    
                    if (webhookData?.Booking != null)
                    {
                        _logger.LogInformation("✅ Successfully parsed booking webhook: {BookingCode}, Status: {Status}", 
                            webhookData.Booking.Code, webhookData.Booking.Status);
                        
                        webhookLog.BookingCode = webhookData.Booking.Code;
                        webhookLog.CustomerEmail = webhookData.Booking.Customer?.Email;
                        webhookLog.IsValid = true;
                        
                        // Create notification message
                        var notification = new
                        {
                            type = "webhook",
                            bookingCode = webhookData.Booking.Code,
                            status = webhookData.Booking.Status,
                            eventType = eventType,
                            timestamp = DateTime.UtcNow,
                            customer = webhookData.Booking.Customer?.Name,
                            isCancellation = webhookData.Booking.Status == "STOP"
                        };

                        _logger.LogInformation("📡 Sending notification to clients: {Notification}", 
                            JsonSerializer.Serialize(notification));

                        // Send real-time notification to all connected clients
                        await _hubContext.Clients.All.SendAsync("ReceiveWebhook", notification);
                        
                        stopwatch.Stop();
                        webhookLog.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                        webhookLog.ProcessingStatus = "Success";
                        webhookLog.ResponseStatusCode = 200;
                        webhookLog.ResponseMessage = "Webhook received and processed successfully";
                        
                        // Add to webhook logs
                        lock (_webhookLogsLock)
                        {
                            _webhookLogs.Add(webhookLog);
                            // Keep only last 1000 logs to prevent memory issues
                            if (_webhookLogs.Count > 1000)
                            {
                                _webhookLogs.RemoveAt(0);
                            }
                        }
                        
                        _logger.LogInformation("✅ Webhook processed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                        _logger.LogInformation("=== CHECKFRONT WEBHOOK COMPLETED ===");
                        
                        // Always return 200 OK to prevent Checkfront from blocking us
                        return Ok(new { 
                            message = "Webhook received and processed", 
                            bookingCode = webhookData.Booking.Code,
                            status = webhookData.Booking.Status,
                            eventType = eventType,
                            processingTimeMs = stopwatch.ElapsedMilliseconds
                        });
                        }
                        else
                        {
                        _logger.LogWarning("⚠️ Webhook payload did not contain booking data: {Payload}", payload);
                        
                        stopwatch.Stop();
                        webhookLog.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                        webhookLog.ProcessingStatus = "Success";
                        webhookLog.ResponseStatusCode = 200;
                        webhookLog.ResponseMessage = "Webhook received but no booking data found";
                        webhookLog.IsValid = false;
                        
                        // Add to webhook logs
                        lock (_webhookLogsLock)
                        {
                            _webhookLogs.Add(webhookLog);
                            if (_webhookLogs.Count > 1000)
                            {
                                _webhookLogs.RemoveAt(0);
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "❌ Error parsing webhook payload: {Payload}", payload);
                    
                    stopwatch.Stop();
                    webhookLog.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                    webhookLog.ProcessingStatus = "Failed";
                    webhookLog.ResponseStatusCode = 200;
                    webhookLog.ResponseMessage = "Webhook received but failed to parse JSON";
                    webhookLog.ErrorMessage = ex.Message;
                    webhookLog.IsValid = false;
                    
                    // Add to webhook logs
                    lock (_webhookLogsLock)
                    {
                        _webhookLogs.Add(webhookLog);
                        if (_webhookLogs.Count > 1000)
                        {
                            _webhookLogs.RemoveAt(0);
                        }
                    }
                    
                    _logger.LogInformation("=== CHECKFRONT WEBHOOK COMPLETED (PARSE ERROR) ===");
                    
                    // Still return 200 OK to prevent Checkfront from blocking us
                    return Ok(new { message = "Webhook received but failed to parse", eventType = eventType, error = ex.Message });
                }

                // If we get here, it's not a booking webhook or parsing failed
                _logger.LogInformation("ℹ️ Webhook received but no booking data found");
                
                stopwatch.Stop();
                webhookLog.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                webhookLog.ProcessingStatus = "Success";
                webhookLog.ResponseStatusCode = 200;
                webhookLog.ResponseMessage = "Webhook received but no booking data found";
                
                // Add to webhook logs
                lock (_webhookLogsLock)
                {
                    _webhookLogs.Add(webhookLog);
                    if (_webhookLogs.Count > 1000)
                    {
                        _webhookLogs.RemoveAt(0);
                    }
                }
                
                _logger.LogInformation("=== CHECKFRONT WEBHOOK COMPLETED ===");
                
                // Still return 200 OK to prevent Checkfront from blocking us
                return Ok(new { message = "Webhook received", eventType = eventType });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing Checkfront webhook");
                
                stopwatch.Stop();
                webhookLog.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                webhookLog.ProcessingStatus = "Failed";
                webhookLog.ResponseStatusCode = 200;
                webhookLog.ResponseMessage = "Webhook received but encountered an error";
                webhookLog.ErrorMessage = ex.Message;
                
                // Add to webhook logs
                lock (_webhookLogsLock)
                {
                    _webhookLogs.Add(webhookLog);
                    if (_webhookLogs.Count > 1000)
                    {
                        _webhookLogs.RemoveAt(0);
                    }
                }
                
                _logger.LogInformation("=== CHECKFRONT WEBHOOK COMPLETED (ERROR) ===");
                
                // Even on error, return 200 OK to prevent Checkfront from blocking us
                return Ok(new { message = "Webhook received but encountered an error", error = ex.Message });
            }
        }

        /// <summary>
        /// Test webhook endpoint - for testing webhook functionality
        /// </summary>
        [HttpPost("webhook/test")]
        public async Task<IActionResult> TestWebhook([FromBody] object testData)
        {
            var startTime = DateTime.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogInformation("=== TEST WEBHOOK RECEIVED ===");
            _logger.LogInformation("🧪 Test webhook endpoint hit at {Time}", startTime);
            _logger.LogInformation("Request IP: {IP}", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            _logger.LogInformation("User Agent: {UserAgent}", Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown");
            
            try
            {
                var serializedData = JsonSerializer.Serialize(testData);
                _logger.LogInformation("🧪 Test webhook received: {TestData}", serializedData);
                
                // Create a test webhook log entry
                var testWebhookLog = new WebhookLogInfo
                {
                    Id = _webhookLogs.Count + 1,
                    Timestamp = startTime,
                    EventType = "test",
                    HttpMethod = Request.Method,
                    Endpoint = Request.Path,
                    RawPayload = serializedData,
                    Headers = JsonSerializer.Serialize(Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())),
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    UserAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown",
                    IsValid = true,
                    ProcessingStatus = "Success",
                    ResponseStatusCode = 200,
                    ResponseMessage = "Test webhook received successfully"
                };
                
                stopwatch.Stop();
                testWebhookLog.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
                
                // Add to webhook logs
                lock (_webhookLogsLock)
                {
                    _webhookLogs.Add(testWebhookLog);
                    if (_webhookLogs.Count > 1000)
                    {
                        _webhookLogs.RemoveAt(0);
                    }
                }
                
                // Send real-time notification to connected clients
                var notification = new
                {
                    type = "test-webhook",
                    eventType = "test",
                    timestamp = DateTime.UtcNow,
                    data = testData,
                    processingTimeMs = stopwatch.ElapsedMilliseconds
                };
                
                await _hubContext.Clients.All.SendAsync("ReceiveWebhook", notification);
                
                _logger.LogInformation("✅ Test webhook processed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                _logger.LogInformation("=== TEST WEBHOOK COMPLETED ===");
                
                return Ok(new 
                { 
                    message = "Test webhook received successfully",
                    timestamp = DateTime.UtcNow,
                    data = testData,
                    processingTimeMs = stopwatch.ElapsedMilliseconds,
                    logId = testWebhookLog.Id
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(ex, "❌ Error in test webhook");
                
                // Create error log entry
                var errorWebhookLog = new WebhookLogInfo
                {
                    Id = _webhookLogs.Count + 1,
                    Timestamp = startTime,
                    EventType = "test",
                    HttpMethod = Request.Method,
                    Endpoint = Request.Path,
                    RawPayload = testData?.ToString() ?? "",
                    Headers = JsonSerializer.Serialize(Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())),
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    UserAgent = Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown",
                    IsValid = false,
                    ProcessingStatus = "Failed",
                    ResponseStatusCode = 500,
                    ResponseMessage = "Test webhook failed",
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
                
                // Add to webhook logs
                lock (_webhookLogsLock)
                {
                    _webhookLogs.Add(errorWebhookLog);
                    if (_webhookLogs.Count > 1000)
                    {
                        _webhookLogs.RemoveAt(0);
                    }
                }
                
                _logger.LogInformation("=== TEST WEBHOOK COMPLETED (ERROR) ===");
                
                return StatusCode(500, new { 
                    message = "Internal server error", 
                    error = ex.Message,
                    timestamp = DateTime.UtcNow,
                    processingTimeMs = stopwatch.ElapsedMilliseconds
                });
            }
        }

        /// <summary>
        /// Get webhook URL for configuration
        /// </summary>
        [HttpGet("webhook-url")]
        public IActionResult GetWebhookUrl()
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var webhookUrl = $"{baseUrl}/api/checkfront/webhook";
            
            return Ok(new 
            { 
                webhookUrl = webhookUrl,
                testWebhookUrl = $"{baseUrl}/api/checkfront/webhook/test",
                instructions = "Configure this URL in your Checkfront webhook settings"
            });
        }

        /// <summary>
        /// Debug endpoint - Get raw company response from Checkfront
        /// </summary>
        [HttpGet("debug/raw-company")]
        public async Task<IActionResult> GetRawCompanyResponse()
        {
            try
            {
                var rawResponse = await _checkfrontService.GetRawCompanyResponseAsync();
                return Ok(new { rawResponse = rawResponse });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting raw company response");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
} 
