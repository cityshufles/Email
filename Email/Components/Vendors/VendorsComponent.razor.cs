using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Email.Models;
using Email.Services;

namespace Email.Components.Vendors
{
    public partial class VendorsComponent : ComponentBase
    {
        [Inject] protected IVendorsApiService VendorsApi { get; set; } = default!;
        [Inject] protected IToursApiService ToursApi { get; set; } = default!;
        [Inject] protected ILogger<VendorsComponent> Logger { get; set; } = default!;

        private string activeTab = "create";
        private bool isLoading;

        private List<DbVendor> vendors = new();
        private List<DbTour> tours = new();
        private List<string> masterTourNames = new();
        private HashSet<string> activeMasterTourNames = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, HashSet<string>> vendorTourLookup = new();

        private int? editingVendorId;
        private string? vendorName;
        private string? vendorAllToursLink;
        private bool vendorIsActive = true;
        private string? lastVendorError;
        private string? lastVendorTourError;

        private int? selectedVendorId;

        protected override async Task OnInitializedAsync()
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            var previousSelected = selectedVendorId;
            try
            {
                isLoading = true;
                StateHasChanged();

                vendors = await VendorsApi.GetVendorsAsync();
                try
                {
                    tours = await ToursApi.GetToursAsync();
                }
                catch
                {
                    tours = new();
                }

                vendors = vendors
                    .OrderBy(v => v.VendorName)
                    .ToList();

                masterTourNames = tours
                    .Where(t => t.IsActive)
                    .Select(t => t.MasterTourName?.Trim() ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name)
                    .ToList();
                activeMasterTourNames = new HashSet<string>(masterTourNames, StringComparer.OrdinalIgnoreCase);

                try
                {
                    await BackfillVendorToursFromActiveTourAssignmentsAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "VendorsComponent could not backfill vendor tour assignments on load");
                }

                await LoadVendorToursAsync();

                if (previousSelected.HasValue && vendors.Any(v => v.Id == previousSelected.Value))
                {
                    selectedVendorId = previousSelected;
                }
                else
                {
                    selectedVendorId = vendors.FirstOrDefault()?.Id;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "VendorsComponent failed to load data");
            }
            finally
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        private async Task LoadVendorToursAsync()
        {
            var vendorTours = await VendorsApi.GetVendorToursAsync();
            vendorTourLookup = vendorTours
                .Where(vt => vt.IsActive)
                .GroupBy(vt => vt.VendorId)
                .ToDictionary(
                    g => g.Key,
                    g => new HashSet<string>(g.Select(x => x.MasterTourName?.Trim() ?? string.Empty), StringComparer.OrdinalIgnoreCase));
        }

        private async Task BackfillVendorToursFromActiveTourAssignmentsAsync()
        {
            if (vendors.Count == 0 || tours.Count == 0)
            {
                return;
            }

            var vendorIdByKey = vendors
                .Where(v => v.Id > 0)
                .GroupBy(v => NormalizeVendorKey(v.VendorName), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderByDescending(v => v.UpdatedAt ?? v.CreatedAt ?? DateTime.MinValue)
                        .ThenByDescending(v => v.Id)
                        .First()
                        .Id,
                    StringComparer.OrdinalIgnoreCase);

            if (vendorIdByKey.Count == 0)
            {
                return;
            }

            var existingAssignments = await VendorsApi.GetVendorToursAsync();
            var activeAssignmentKeys = new HashSet<string>(
                existingAssignments
                    .Where(vt => vt.IsActive)
                    .Select(vt => BuildVendorTourKey(vt.VendorId, vt.MasterTourName)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var tour in tours.Where(t => t.IsActive))
            {
                var masterTourName = (tour.MasterTourName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(masterTourName))
                {
                    continue;
                }

                foreach (var vendorToken in SplitVendorNames(tour.VendorNames))
                {
                    var vendorKey = NormalizeVendorKey(vendorToken);
                    if (string.IsNullOrWhiteSpace(vendorKey))
                    {
                        continue;
                    }

                    if (!vendorIdByKey.TryGetValue(vendorKey, out var vendorId))
                    {
                        continue;
                    }

                    var assignmentKey = BuildVendorTourKey(vendorId, masterTourName);
                    if (activeAssignmentKeys.Contains(assignmentKey))
                    {
                        continue;
                    }

                    var ok = await VendorsApi.SetVendorTourActiveAsync(vendorId, masterTourName, true);
                    if (ok)
                    {
                        activeAssignmentKeys.Add(assignmentKey);
                    }
                    else
                    {
                        Logger.LogWarning(
                            "VendorsComponent failed to backfill vendor tour assignment. VendorId={VendorId}, MasterTourName={MasterTourName}, Error={Error}",
                            vendorId,
                            masterTourName,
                            VendorsApi.LastError);
                    }
                }
            }
        }

        private static string BuildVendorTourKey(int vendorId, string? masterTourName)
        {
            var masterKey = string.IsNullOrWhiteSpace(masterTourName)
                ? string.Empty
                : masterTourName.Trim().ToUpperInvariant();

            return $"{vendorId}|{masterKey}";
        }

        private static IEnumerable<string> SplitVendorNames(string? rawVendorNames)
        {
            if (string.IsNullOrWhiteSpace(rawVendorNames))
            {
                yield break;
            }

            var parts = rawVendorNames.Split(
                new[] { ',', ';', '|' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part.Trim();
                }
            }
        }

        private static string NormalizeVendorKey(string? vendorName)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
            {
                return string.Empty;
            }

            var upper = vendorName.Trim().ToUpperInvariant();
            var compact = new string(upper.Where(char.IsLetterOrDigit).ToArray());

            if (compact.Contains("VIATOR", StringComparison.Ordinal))
            {
                return "VIATOR";
            }

            if (compact.Contains("GURUWALK", StringComparison.Ordinal) || compact.StartsWith("GURU", StringComparison.Ordinal))
            {
                return "GURUWALK";
            }

            if (compact.Contains("FREETOUR", StringComparison.Ordinal))
            {
                return "FREETOUR";
            }

            if (compact.Contains("GETYOURGUIDE", StringComparison.Ordinal) || compact.Equals("GYG", StringComparison.Ordinal))
            {
                return "GETYOURGUIDE";
            }

            if (compact.Contains("AIRBNB", StringComparison.Ordinal))
            {
                return "AIRBNB";
            }

            if (compact.Contains("CIVITATIS", StringComparison.Ordinal) || compact.Contains("CIVATASIS", StringComparison.Ordinal))
            {
                return "CIVITATIS";
            }

            if (compact.Contains("CITYSHUFFLES", StringComparison.Ordinal) || compact.Contains("WEBSITE", StringComparison.Ordinal))
            {
                return "WEBSITE";
            }

            return compact;
        }

        private DbVendor? SelectedVendor => selectedVendorId.HasValue
            ? vendors.FirstOrDefault(v => v.Id == selectedVendorId.Value)
            : null;

        private void SetTab(string tab)
        {
            activeTab = tab == "list" ? "list" : "create";
        }

        private void SelectVendor(int vendorId)
        {
            selectedVendorId = vendorId;
        }

        private int GetVendorTourCount(int vendorId)
        {
            if (!vendorTourLookup.TryGetValue(vendorId, out var set))
            {
                return 0;
            }

            return set.Count(name => activeMasterTourNames.Contains(name));
        }

        private bool IsVendorTourSelected(int vendorId, string masterTourName)
        {
            return vendorTourLookup.TryGetValue(vendorId, out var set) && set.Contains(masterTourName);
        }

        private async Task ToggleVendorTourAsync(int vendorId, string masterTourName, ChangeEventArgs e)
        {
            var isChecked = e.Value switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                string s when string.Equals(s, "on", StringComparison.OrdinalIgnoreCase) => true,
                _ => false
            };

            lastVendorTourError = null;

            var ok = await VendorsApi.SetVendorTourActiveAsync(vendorId, masterTourName, isChecked);
            if (ok)
            {
                if (!vendorTourLookup.TryGetValue(vendorId, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    vendorTourLookup[vendorId] = set;
                }

                if (isChecked)
                {
                    set.Add(masterTourName);
                }
                else
                {
                    set.Remove(masterTourName);
                }
            }
            else
            {
                lastVendorTourError = VendorsApi.LastError ?? "Unknown error updating vendor tours.";
                await LoadVendorToursAsync();
            }
        }

        private async Task SaveVendorAsync()
        {
            if (string.IsNullOrWhiteSpace(vendorName)) return;

            var payload = new DbVendor
            {
                VendorName = vendorName.Trim(),
                AllToursLink = string.IsNullOrWhiteSpace(vendorAllToursLink) ? null : vendorAllToursLink.Trim(),
                IsActive = vendorIsActive
            };

            lastVendorError = null;

            if (editingVendorId.HasValue)
            {
                var ok = await VendorsApi.UpdateVendorAsync(editingVendorId.Value, payload);
                if (ok)
                {
                    await LoadDataAsync();
                    ClearVendorForm();
                }
                else
                {
                    lastVendorError = VendorsApi.LastError ?? "Unknown error updating vendor.";
                }
            }
            else
            {
                var created = await VendorsApi.CreateVendorAsync(payload);
                if (created != null)
                {
                    await LoadDataAsync();
                    selectedVendorId = created.Id;
                    ClearVendorForm();
                }
                else
                {
                    lastVendorError = VendorsApi.LastError ?? "Unknown error creating vendor.";
                }
            }
        }

        private void EditVendorById(int vendorId)
        {
            var vendor = vendors.FirstOrDefault(v => v.Id == vendorId);
            if (vendor == null) return;

            editingVendorId = vendor.Id;
            vendorName = vendor.VendorName;
            vendorAllToursLink = vendor.AllToursLink;
            vendorIsActive = vendor.IsActive;
            lastVendorError = null;
            activeTab = "create";
        }

        private void CancelVendorEdit()
        {
            editingVendorId = null;
            ClearVendorForm();
        }

        private void ClearVendorForm()
        {
            vendorName = null;
            vendorAllToursLink = null;
            vendorIsActive = true;
            editingVendorId = null;
            lastVendorError = null;
        }
    }
}
