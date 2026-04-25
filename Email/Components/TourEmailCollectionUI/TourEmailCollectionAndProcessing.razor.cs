using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Grids;
using Email.Models;
using Email.Services;

namespace Email.Components.TourEmailCollectionUI
{
    // Created: 2025-09-15 00:00 UTC - Code-behind for TourEmailCollectionAndProcessing
    public class TourEmailCollectionAndProcessingBase : ComponentBase
    {
        [Parameter] public bool HideCollectionToolbar { get; set; } = false;
        [Inject] protected ITourEmailsService TourEmailsService { get; set; } = default!;
        protected SfGrid<UnprocessedEmailDto>? unprocessedEmailsGrid;
        /// <summary>
        /// 9 16 25 2:12pm - Raised when a collection or processing action completes.
        /// </summary>
        [Parameter] public EventCallback OnProcessingCompleted { get; set; }

        protected bool isBusy;
        protected bool hasSelectedEmails;
        private bool _onlyUnclassified = false;
        protected bool onlyUnclassified
        {
            get => _onlyUnclassified;
            set
            {
                if (_onlyUnclassified != value)
                {
                    _onlyUnclassified = value;
                    _ = RefreshGridAsync();
                }
            }
        }
        private bool _onlyClassified = false;
        protected bool onlyClassified
        {
            get => _onlyClassified;
            set
            {
                if (_onlyClassified != value)
                {
                    _onlyClassified = value;
                    _ = RefreshGridAsync();
                }
            }
        }
        protected string searchTerm = string.Empty;
        private bool _onlyTourRelated = false;
        protected bool onlyTourRelated
        {
            get => _onlyTourRelated;
            set
            {
                if (_onlyTourRelated != value)
                {
                    _onlyTourRelated = value;
                    _ = RefreshGridAsync();
                }
            }
        }
        protected List<UnprocessedEmailDto> unprocessedEmails = new();
        protected CollectionStatusDto? collectionStatus;

        protected override async Task OnInitializedAsync()
        {
            await RefreshStatusAsync();
            await LoadUnprocessedEmailsAsync();
        }

        protected async Task RefreshStatusAsync()
        {
            collectionStatus = await TourEmailsService.GetCollectionStatusAsync();
            StateHasChanged();
        }

        protected async Task LoadUnprocessedEmailsAsync()
        {
            isBusy = true;
            StateHasChanged();
            try
            {
                unprocessedEmails = await TourEmailsService.GetUnprocessedEmailsAsync(50, 0, null, string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm, onlyUnclassified, onlyTourRelated, onlyClassified);
                hasSelectedEmails = false;
                // Reset selection state when loading new data
                if (unprocessedEmailsGrid != null)
                {
                    await unprocessedEmailsGrid.ClearSelectionAsync();
                }
            }
            finally
            {
                isBusy = false;
            }
        }

        protected async Task RefreshGridAsync()
        {
            searchTerm = searchTerm?.Trim() ?? string.Empty;
            await LoadUnprocessedEmailsAsync();
            await RefreshStatusAsync();
            // Update selection state after grid refresh
            await UpdateSelectionState();
        }

        protected async Task CollectOnlyAsync()
        {
            if (isBusy) return;
            isBusy = true;
            StateHasChanged();
            try
            {
                await TourEmailsService.CollectEmailsOnlyAsync("today");
            }
            finally
            {
                isBusy = false;
            }
            await RefreshGridAsync();
            if (OnProcessingCompleted.HasDelegate)
            {
                await OnProcessingCompleted.InvokeAsync();
            }
        }

        protected async Task CollectWithSpan(string span)
        {
            if (isBusy) return;
            isBusy = true;
            StateHasChanged();
            try
            {
                // Map UI span to API expected casing
                var mapped = span.ToLowerInvariant() switch
                {
                    "new" => "New",
                    "today" => "Today",
                    "week" => "Week",
                    "month" => "Month",
                    "all" => "All",
                    "last50" => "Last50",
                    "last100" => "Last100",
                    _ => "Today"
                };
                await TourEmailsService.CollectEmailsOnlyAsync(mapped);
            }
            finally
            {
                isBusy = false;
            }
            await RefreshGridAsync();
            if (OnProcessingCompleted.HasDelegate)
            {
                await OnProcessingCompleted.InvokeAsync();
            }
        }

        protected async Task CollectAndProcessSpanAsync(string span)
        {
            if (isBusy) return;
            isBusy = true;
            StateHasChanged();
            try
            {
                var mapped = span.ToLowerInvariant() switch
                {
                    "new" => "New",
                    "today" => "Today",
                    "week" => "Week",
                    "month" => "Month",
                    "all" => "All",
                    "last50" => "Last50",
                    "last100" => "Last100",
                    _ => "Today"
                };
                await TourEmailsService.ProcessCustomSpanAsync(mapped);
            }
            finally
            {
                isBusy = false;
            }
            await RefreshGridAsync();
            if (OnProcessingCompleted.HasDelegate)
            {
                await OnProcessingCompleted.InvokeAsync();
            }
        }

        protected async Task ProcessSelectedAsync()
        {
            if (isBusy || unprocessedEmailsGrid == null) return;
            var selected = await unprocessedEmailsGrid.GetSelectedRecordsAsync();
            var ids = selected?.Select(x => x.Id).ToList() ?? new List<int>();
            if (ids.Count == 0) return;

            isBusy = true;
            StateHasChanged();
            try
            {
                await TourEmailsService.ProcessSpecificEmailsAsync(ids);
            }
            finally
            {
                isBusy = false;
            }
            await RefreshGridAsync();
            if (OnProcessingCompleted.HasDelegate)
            {
                await OnProcessingCompleted.InvokeAsync();
            }
        }

        protected async Task ProcessSingleEmail(UnprocessedEmailDto? email)
        {
            if (email == null) return;
            await ProcessSpecificEmailsAsync(new List<int> { email.Id });
            if (OnProcessingCompleted.HasDelegate)
            {
                await OnProcessingCompleted.InvokeAsync();
            }
        }

        private async Task ProcessSpecificEmailsAsync(List<int> ids)
        {
            if (ids.Count == 0) return;
            isBusy = true;
            StateHasChanged();
            try
            {
              //  await TourEmailsApiService.ProcessSpecificEmailsAsync(ids);
            }
            finally
            {
                isBusy = false;
            }
            await RefreshGridAsync();
        }

        protected bool showPreview = false;
        protected InboxEmailDetailDto? previewEmail;

        protected async Task PreviewEmail(UnprocessedEmailDto? email)
        {
            if (email == null) return;
            isBusy = true;
            StateHasChanged();
            try
            {
                previewEmail = await TourEmailsService.GetInboxEmailByIdAsync(email.Id);
                showPreview = previewEmail != null;
            }
            finally
            {
                isBusy = false;
            }
        }

        protected Task ClosePreview()
        {
            showPreview = false;
            previewEmail = null;
            StateHasChanged();
            return Task.CompletedTask;
        }

        // Skip removed per UX decision: leave unchecked to skip

        protected async Task OnRowSelected(RowSelectEventArgs<UnprocessedEmailDto> args)
        {
            await UpdateSelectionState();
        }

        protected async Task OnRowDeselected(RowDeselectEventArgs<UnprocessedEmailDto> args)
        {
            await UpdateSelectionState();
        }

        private async Task UpdateSelectionState()
        {
            if (unprocessedEmailsGrid == null)
            {
                hasSelectedEmails = false;
                StateHasChanged();
                return;
            }
            
            try
            {
                var selected = await unprocessedEmailsGrid.GetSelectedRecordsAsync();
                hasSelectedEmails = selected != null && selected.Count > 0;
                
                // Force UI update on correct thread
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                hasSelectedEmails = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        protected async Task PerformSearch()
        {
            await RefreshGridAsync();
        }

        protected async Task ClearSearch()
        {
            searchTerm = string.Empty;
            await RefreshGridAsync();
        }

        protected async Task OnSearchKeyUp(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(searchTerm))
            {
                await PerformSearch();
            }
        }

    }
}


