using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Email.Models;
using Email.Services;
using Email.Services.GmailProcessing;
using Email.Services.GmailProcessing.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Syncfusion.Blazor.Grids;

namespace Email.Components.TourEmailProcessing
{
    // Created: 2025-11-25 00:00 UTC - Processing/Edit panel (no AI)
    public class TourEmailProcessingPanelBase : ComponentBase
    {
        protected enum GridMode { Unprocessed, Processed, Problems }

        [Inject] protected ITourEmailsService TourEmailsService { get; set; } = default!;
        [Inject] protected GmailProcessingV2Service ProcessingService { get; set; } = default!;
        [Inject] protected ILogger<TourEmailProcessingPanelBase> Logger { get; set; } = default!;

        protected GridMode _mode = GridMode.Unprocessed;
        protected bool _busy;
        protected bool _hasSelection;
        protected string _searchTerm = string.Empty;
        protected int _rowCount = 0;

        protected SfGrid<UnprocessedEmailDto>? _unprocessedGrid;
        protected SfGrid<ProcessedEmailDisplayTourEmailLibModel>? _processedGrid;
        protected SfGrid<ProcessedEmailDisplayDto>? _problemsGrid;

        protected List<UnprocessedEmailDto> _unprocessed = new();
        protected List<ProcessedEmailDisplayTourEmailLibModel> _processed = new();
        protected List<ProcessedEmailDisplayDto> _problems = new();

        protected override async Task OnInitializedAsync()
        {
            await LoadAsync();
        }

        protected async Task SetMode(GridMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            await LoadAsync();
        }

        protected async Task SearchAsync()
        {
            await LoadAsync();
        }

        protected async Task ClearSearchAsync()
        {
            _searchTerm = string.Empty;
            await LoadAsync();
        }

        protected async Task RefreshAsync()
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _busy = true;
            _hasSelection = false;
            _rowCount = 0;
            StateHasChanged();
            try
            {
                switch (_mode)
                {
                    case GridMode.Unprocessed:
                        await LoadUnprocessedAsync();
                        break;
                    case GridMode.Processed:
                        await LoadProcessedAsync();
                        break;
                    case GridMode.Problems:
                        await LoadProblemsAsync();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[TourEmailProcessingPanel] Load failed: {Message}", ex.Message);
            }
            finally
            {
                _busy = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task LoadUnprocessedAsync()
        {
            var search = string.IsNullOrWhiteSpace(_searchTerm) ? null : _searchTerm.Trim();
            _unprocessed = await TourEmailsService.GetUnprocessedEmailsAsync(50, 0, null, search, false, true, false);
            _rowCount = _unprocessed.Count;
            if (_unprocessedGrid != null)
            {
                try { await _unprocessedGrid.ClearSelectionAsync(); } catch { }
            }
        }

        private async Task LoadProcessedAsync()
        {
            _processed = await TourEmailsService.GetProcessedEmailsForGridAsync(50, 0, string.IsNullOrWhiteSpace(_searchTerm) ? null : _searchTerm.Trim());
            _rowCount = _processed.Count;
            if (_processedGrid != null)
            {
                try { await _processedGrid.ClearSelectionAsync(); } catch { }
            }
        }

        private async Task LoadProblemsAsync()
        {
            // show recent skipped/errors
            var list = await ProcessingService.GetRecentSkippedAsync(limit: 100, ct: default);
            // Optional quick filter by search
            if (!string.IsNullOrWhiteSpace(_searchTerm))
            {
                var s = _searchTerm.Trim();
                list = list
                    .Where(x =>
                        (x.MessageId?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (x.CustomerName?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (x.BookingCode?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (x.EmailType?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            }
            _problems = list.ToList();
            _rowCount = _problems.Count;
            if (_problemsGrid != null)
            {
                try { await _problemsGrid.ClearSelectionAsync(); } catch { }
            }
        }

        protected async Task ProcessSelectedAsync()
        {
            if (_mode != GridMode.Unprocessed || _unprocessedGrid == null) return;
            var selected = await _unprocessedGrid.GetSelectedRecordsAsync();
            var inboxIds = selected?.Select(x => x.Id).Distinct().ToList() ?? new List<int>();
            if (inboxIds.Count == 0) return;

            _busy = true;
            StateHasChanged();
            try
            {
                await ProcessingService.ProcessSpecificAsync(inboxIds, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[TourEmailProcessingPanel] ProcessSelected failed: {Message}", ex.Message);
            }
            finally
            {
                _busy = false;
            }
            await LoadAsync();
        }

        protected async Task ReprocessSelectedAsync()
        {
            if (_mode == GridMode.Processed && _processedGrid != null)
            {
                var selected = await _processedGrid.GetSelectedRecordsAsync();
                var processedIds = selected?.Select(x => x.Id).Distinct().ToList() ?? new List<int>();
                if (processedIds.Count == 0) return;

                _busy = true;
                StateHasChanged();
                try
                {
                    var inboxIds = new List<int>();
                    foreach (var pid in processedIds)
                    {
                        try
                        {
                            var pe = await ProcessingService.GetProcessedEmailAsync(pid, CancellationToken.None);
                            if (pe != null && pe.InboxEmailId > 0)
                            {
                                inboxIds.Add(pe.InboxEmailId);
                            }
                        }
                        catch
                        {
                            // ignore individual failures
                        }
                    }

                    if (inboxIds.Count > 0)
                    {
                        await ProcessingService.ProcessSpecificAsync(inboxIds.Distinct().ToList(), CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[TourEmailProcessingPanel] ReprocessSelected (processed) failed: {Message}", ex.Message);
                }
                finally
                {
                    _busy = false;
                }
                await LoadAsync();
                return;
            }

            if (_mode == GridMode.Problems && _problemsGrid != null)
            {
                var selected = await _problemsGrid.GetSelectedRecordsAsync();
                var inboxIds = selected?
                    .Where(x => x != null && x.InboxEmailId > 0)
                    .Select(x => x.InboxEmailId)
                    .Distinct()
                    .ToList() ?? new List<int>();
                if (inboxIds.Count == 0) return;

                _busy = true;
                StateHasChanged();
                try
                {
                    await ProcessingService.ProcessSpecificAsync(inboxIds, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[TourEmailProcessingPanel] ReprocessSelected (problems) failed: {Message}", ex.Message);
                }
                finally
                {
                    _busy = false;
                }
                await LoadAsync();
            }
        }

        protected async Task OnUnprocessedRowSelected(RowSelectEventArgs<UnprocessedEmailDto> args) => await UpdateSelectionAsync();
        protected async Task OnUnprocessedRowDeselected(RowDeselectEventArgs<UnprocessedEmailDto> args) => await UpdateSelectionAsync();
        protected async Task OnProcessedRowSelected(RowSelectEventArgs<ProcessedEmailDisplayTourEmailLibModel> args) => await UpdateSelectionAsync();
        protected async Task OnProcessedRowDeselected(RowDeselectEventArgs<ProcessedEmailDisplayTourEmailLibModel> args) => await UpdateSelectionAsync();
        protected async Task OnProblemsRowSelected(RowSelectEventArgs<ProcessedEmailDisplayDto> args) => await UpdateSelectionAsync();
        protected async Task OnProblemsRowDeselected(RowDeselectEventArgs<ProcessedEmailDisplayDto> args) => await UpdateSelectionAsync();

        private async Task UpdateSelectionAsync()
        {
            try
            {
                if (_mode == GridMode.Unprocessed)
                {
                    if (_unprocessedGrid == null) { _hasSelection = false; return; }
                    var s = await _unprocessedGrid.GetSelectedRecordsAsync();
                    _hasSelection = s != null && s.Count > 0;
                    await InvokeAsync(StateHasChanged);
                    return;
                }
                if (_mode == GridMode.Processed)
                {
                    if (_processedGrid == null) { _hasSelection = false; return; }
                    var s = await _processedGrid.GetSelectedRecordsAsync();
                    _hasSelection = s != null && s.Count > 0;
                    await InvokeAsync(StateHasChanged);
                    return;
                }
                if (_mode == GridMode.Problems)
                {
                    if (_problemsGrid == null) { _hasSelection = false; return; }
                    var s = await _problemsGrid.GetSelectedRecordsAsync();
                    _hasSelection = s != null && s.Count > 0;
                    await InvokeAsync(StateHasChanged);
                    return;
                }
            }
            catch
            {
                _hasSelection = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }
}


