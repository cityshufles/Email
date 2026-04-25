using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Schedule;
using Syncfusion.Blazor.DropDowns;
using Email.Calendar.Models;
using Email.Calendar.Services;

namespace Email.Calendar.Components
{
    public partial class CalendarScheduler : ComponentBase
    {
        [Inject] private ICalendarDataService CalendarService { get; set; } = default!;
        [Inject] private IGoogleCalendarService GoogleCalendarService { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        private DateTime CurrentDate { get; set; } = new DateTime(2025, 12, 23); // Align with demo data
        private List<CalendarAppointmentModel> AllAppointments { get; set; } = new();
        private List<CalendarAppointmentModel> Appointments { get; set; } = new();
        private List<GoogleAppointmentModel> GoogleAppointments { get; set; } = new();
        private List<string> GuideNames { get; set; } = new();
        private string? SelectedGuideFilter { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            // Load data for a wide range to cover typical demo usage
            var start = new DateTime(2025, 1, 1);
            var end = new DateTime(2027, 12, 31);

            AllAppointments = (await CalendarService.GetAppointmentsAsync(start, end)).ToList();
            Appointments = AllAppointments; // Initial view has no filter

            try 
            {
                Console.WriteLine("CalendarScheduler: Loading Google Events...");
                GoogleAppointments = await GoogleCalendarService.GetEventsAsync(start, end);
                Console.WriteLine($"CalendarScheduler: Loaded {GoogleAppointments.Count} Google Events.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CalendarScheduler: Error loading Google events: {ex.Message}");
                // Optionally log or handle if desired, usually service logs errors
                GoogleAppointments = new List<GoogleAppointmentModel>();
            }

            // Load Dictionary of guides for filter
            var guides = await CalendarService.GetGuidesAsync();
            GuideNames = guides.Select(g => $"{g.FirstName} {g.LastName}").ToList();
            GuideNames.Insert(0, "Unassigned"); // Add "No Guide" / "Unassigned" option
        }

        private async Task OnGuideFilterChange(ChangeEventArgs<string, string> args)
        {
            var start = new DateTime(2025, 1, 1);
            var end = new DateTime(2027, 12, 31);
            
            Console.WriteLine($"[CalendarScheduler] Filter Changed: {args.Value}");

            if (string.IsNullOrEmpty(args.Value))
            {
                // Reset to all
                Appointments = (await CalendarService.GetAppointmentsAsync(start, end)).ToList();
            }
            else
            {
                // Server-side filter
                Appointments = (await CalendarService.GetAppointmentsAsync(start, end, args.Value)).ToList();
            }
            
            // Force refresh if needed, usually Blazor handles it on bind update
        }

        private void OnAppointmentClick(EventClickArgs<CalendarAppointmentModel> args)
        {
            // Example navigation or interaction
            // Nav.NavigateTo($"/tours/{args.Event.Id}");
        }
    }
}
