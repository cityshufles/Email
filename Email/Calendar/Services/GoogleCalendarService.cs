using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Email.Calendar.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Email.Calendar.Services
{
    public class GoogleCalendarService : IGoogleCalendarService
    {
        private readonly CalendarService _service;
        private readonly ILogger<GoogleCalendarService> _logger;
        private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment _env;
        private readonly string _calendarId;

        public GoogleCalendarService(IConfiguration configuration, ILogger<GoogleCalendarService> logger, Microsoft.AspNetCore.Hosting.IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
            
            // USE THE PROVEN CONFIGURATION from the API project
            // 1. Try to get the specific Group Calendar ID from config (GoogleCalendar:CalendarId)
            // 2. Fallback to Gmail:Email (which might work if shared, but Group Id is preferred)
            // 3. Fallback to "primary" (which is the SA's own calendar)
            _calendarId = configuration["GoogleCalendar:CalendarId"] 
                       ?? configuration["Gmail:Email"] 
                       ?? "primary";
            
            Console.WriteLine($"GoogleCalendarService: Using Calendar ID: {_calendarId}");

            try
            {
                // Respect config flag so third-party/dev handoff can disable calendar integration.
                var useServiceAccount = bool.TryParse(configuration["GoogleCalendar:UseServiceAccount"], out var enabled) && enabled;
                if (!useServiceAccount)
                {
                    const string disabledMsg = "Google Calendar service account integration is disabled by configuration.";
                    _logger.LogInformation(disabledMsg);
                    Console.WriteLine(disabledMsg);
                    return;
                }

                // Resolve credential file path from config. Supports relative path from content root.
                var configuredPath = (configuration["GoogleCalendar:ServiceAccountKeyPath"] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    const string missingPathMsg = "GoogleCalendar:ServiceAccountKeyPath is empty. Google Calendar features will be disabled.";
                    _logger.LogWarning(missingPathMsg);
                    Console.WriteLine(missingPathMsg);
                    return;
                }

                var credPath = Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(_env.ContentRootPath, configuredPath);
                
                Console.WriteLine($"Identifying Google Credential at: {credPath}");

                if (File.Exists(credPath))
                {
                    GoogleCredential credential;
                    using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
                    {
                        credential = GoogleCredential.FromStream(stream)
                            .CreateScoped(CalendarService.Scope.Calendar);
                    }

                    _service = new CalendarService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = configuration["GoogleCalendar:ApplicationName"] ?? "Email",
                    });
                    
                    Console.WriteLine("Google Calendar Service successfully initialized.");
                }
                else
                {
                    string msg = $"Google Calendar credentials not found at {credPath}. Google Calendar features will be disabled.";
                    _logger.LogWarning(msg);
                    Console.WriteLine(msg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Google Calendar Service");
                Console.WriteLine($"EXCEPTION initializing Google Calendar Service: {ex.Message}");
            }
        }

        public async Task<List<GoogleAppointmentModel>> GetEventsAsync(DateTime start, DateTime end)
        {
            if (_service == null) 
            {
                _logger.LogWarning("Google Calendar Service is not initialized.");
                Console.WriteLine("Google Calendar Service is not initialized (Service is null).");
                return new List<GoogleAppointmentModel>();
            }

            try
            {
                Console.WriteLine($"Fetching Google Events from {_calendarId} between {start} and {end}...");
                var request = _service.Events.List(_calendarId);
                request.TimeMin = start;
                request.TimeMax = end;
                request.ShowDeleted = false;
                request.SingleEvents = true;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

                var events = await request.ExecuteAsync();

                if (events.Items != null && events.Items.Any())
                {
                    Console.WriteLine($"Found {events.Items.Count} Google events.");
                }
                else
                {
                    Console.WriteLine("No Google events found in the responses.");
                }

                return events.Items?.Select(e => new GoogleAppointmentModel
                {
                    Id = e.Id,
                    Summary = e.Summary ?? "(No Title)",
                    Description = e.Description,
                    Location = e.Location,
                    StartTime = e.Start.DateTimeDateTimeOffset?.DateTime ?? (DateTime.TryParse(e.Start.Date, out var d) ? d : DateTime.MinValue), 
                    EndTime = e.End.DateTimeDateTimeOffset?.DateTime ?? (DateTime.TryParse(e.End.Date, out var d2) ? d2 : DateTime.MinValue),
                    IsAllDay = e.Start.Date != null,
                    Etag = e.ETag,
                    ColorId = e.ColorId
                }).ToList() ?? new List<GoogleAppointmentModel>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Google events");
                Console.WriteLine($"EXCEPTION fetching Google events: {ex.Message}");
                return new List<GoogleAppointmentModel>();
            }
        }

        public async Task<GoogleAppointmentModel?> GetEventAsync(string eventId)
        {
            if (_service == null) return null;
            try
            {
                var googleEvent = await _service.Events.Get(_calendarId, eventId).ExecuteAsync();
                if (googleEvent == null) return null;

                return new GoogleAppointmentModel
                {
                    Id = googleEvent.Id,
                    Subject = googleEvent.Summary,
                    Description = googleEvent.Description,
                    Location = googleEvent.Location,
                    StartTime = googleEvent.Start.DateTimeDateTimeOffset?.DateTime ?? (DateTime.TryParse(googleEvent.Start.Date, out var d) ? d : DateTime.MinValue),
                    EndTime = googleEvent.End.DateTimeDateTimeOffset?.DateTime ?? (DateTime.TryParse(googleEvent.End.Date, out var d2) ? d2 : DateTime.MinValue),
                    IsAllDay = googleEvent.Start.Date != null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Google event {Id}", eventId);
                Console.WriteLine($"Error getting Google event {eventId}: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> InsertEventAsync(GoogleAppointmentModel eventData)
        {
            if (_service == null) return null;

            try
            {
                Console.WriteLine($"Inserting Google Event: {eventData.Subject}");
                EventDateTime start, end;
                if (eventData.IsAllDay)
                {
                    start = new EventDateTime() { Date = eventData.StartTime.ToString("yyyy-MM-dd") };
                    end = new EventDateTime() { Date = eventData.EndTime.ToString("yyyy-MM-dd") };
                }
                else
                {
                    start = new EventDateTime() { DateTime = eventData.StartTime };
                    end = new EventDateTime() { DateTime = eventData.EndTime };
                }

                Event eventItem = new Event()
                {
                    Summary = eventData.Subject,
                    Location = eventData.Location,
                    Description = eventData.Description,
                    Start = start,
                    End = end
                };

                var request = _service.Events.Insert(eventItem, _calendarId);
                var createdEvent = await request.ExecuteAsync();
                Console.WriteLine($"Event Created. ID: {createdEvent.Id}");
                return createdEvent.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting Google event");
                Console.WriteLine($"Error inserting Google event: {ex.Message}");
                return null;
            }
        }

        public async Task UpdateEventAsync(GoogleAppointmentModel eventData)
        {
            if (_service == null) return;

            try
            {
                Console.WriteLine($"Updating Google Event {eventData.Id}: {eventData.Subject}");
                var editedEvent = await _service.Events.Get(_calendarId, eventData.Id).ExecuteAsync();
                if (editedEvent == null)
                {
                    Console.WriteLine($"Event {eventData.Id} not found, cannot update.");
                    return;
                }

                editedEvent.Summary = eventData.Subject;
                editedEvent.Location = eventData.Location;
                editedEvent.Description = eventData.Description;
                
                if (eventData.IsAllDay)
                {
                    editedEvent.Start = new EventDateTime() { Date = eventData.StartTime.ToString("yyyy-MM-dd") };
                    editedEvent.End = new EventDateTime() { Date = eventData.EndTime.ToString("yyyy-MM-dd") };
                }
                else
                {
                    editedEvent.Start = new EventDateTime() { DateTime = eventData.StartTime };
                    editedEvent.End = new EventDateTime() { DateTime = eventData.EndTime };
                }

                var request = _service.Events.Update(editedEvent, _calendarId, eventData.Id);
                await request.ExecuteAsync();
                Console.WriteLine($"Event {eventData.Id} Updated.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating Google event");
                Console.WriteLine($"Error updating Google event: {ex.Message}");
            }
        }

        public async Task RemoveEventAsync(string id)
        {
            if (_service == null) return;

            try
            {
                Console.WriteLine($"Deleting Google Event {id}");
                var request = _service.Events.Delete(_calendarId, id);
                await request.ExecuteAsync();
                Console.WriteLine($"Event {id} Deleted.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Google event");
                Console.WriteLine($"Error deleting Google event: {ex.Message}");
            }
        }
    }
}
