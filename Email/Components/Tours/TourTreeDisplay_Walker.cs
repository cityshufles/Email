using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Email.Models;
using Email.TourTreeViewShapedData.Enums;
using Email.TourTreeViewShapedData.Models;

namespace Email.Components.Tours
{
    public partial class TourTreeDisplay : ComponentBase 
    {








        // 2025-12-17 00:00 UTC - Fields for tracking vendor node in dialogs (needed for message regeneration)
        private ShapedTreeNode? selectedSmsVendorNode;
        private ShapedTreeNode? selectedWhatsAppVendorNode;

        // 2025-11-21 00:00 UTC - Open Edit Dialog (shaped-node aware, verbatim behavior)
        //private async Task OpenEditDialog(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        //{
        //    try
        //    {
        //        try { await JSRuntime.InvokeVoidAsync("console.log", $"OpenEditDialog: walker={walkerNode?.Label}"); } catch { }

        //        selectedEditWalkerNode = walkerNode;
        //        selectedEditVendorNode = vendorNode;
        //        selectedEditTourNode = tourNode;

        //        var messageId = walkerNode?.WalkerData?.MessageId;
        //        if (string.IsNullOrWhiteSpace(messageId))
        //        {
        //            // await ToastService.ShowWarningAsync("Edit Not Available", "No booking information found for this walker.");
        //            return;
        //        }

        //        showEditDialog = true;
        //        isEditLoading = true;
        //        StateHasChanged();

        //        try
        //        {
        //            var catalogTask = EnsureCatalogAsync();
        //            var processedEmailTask = TourTreeService.GetProcessedEmailByMessageIdAsync(messageId);
        //            await Task.WhenAll(catalogTask, processedEmailTask);

        //            var response = await processedEmailTask;
        //            if (response != null)
        //            {
        //                selectedProcessedEmail = response;
        //                selectedProcessedEmail.TourName = TourCatalogService.NormalizeName(selectedProcessedEmail.TourName ?? string.Empty);
        //                selectedProcessedEmail.TourTime = NormalizeTimeSafe(selectedProcessedEmail.TourTime);
        //                editTimeOptions = GetTimeOptionsFor(selectedProcessedEmail.TourName);
        //                if (string.IsNullOrWhiteSpace(selectedProcessedEmail.TourTime) && editTimeOptions.Count > 0)
        //                {
        //                    selectedProcessedEmail.TourTime = editTimeOptions.First();
        //                }
        //                if (string.IsNullOrEmpty(selectedProcessedEmail.EmailType) ||
        //                    !new[] { "booking", "modification", "cancellation", "other" }.Contains(selectedProcessedEmail.EmailType))
        //                {
        //                    selectedProcessedEmail.EmailType = "booking";
        //                }
        //            }
        //            else
        //            {
        //                // await ToastService.ShowWarningAsync("Edit Not Available", "No processed email data found for this booking.");
        //                CloseEditDialog();
        //                return;
        //            }
        //        }
        //        finally
        //        {
        //            isEditLoading = false;
        //            StateHasChanged();
        //        }
        //    }
        //    catch
        //    {
        //        showEditDialog = false;
        //        isEditLoading = false;
        //        StateHasChanged();
        //        // await ToastService.ShowErrorAsync("Error", "Failed to open edit dialog.");
        //    }
        //}

        // 2025-11-21 00:00 UTC - Open Edit Dialog (shaped-node aware, verbatim behavior)
        private async Task OpenEditDialog(ShapedTreeNode walkerNode, ShapedTreeNode vendorNode, ShapedTreeNode tourNode)
        {
            try
            {
                try { await JSRuntime.InvokeVoidAsync("console.log", $"OpenEditDialog: walker={walkerNode?.Label}"); } catch { }

                selectedEditWalkerNode = walkerNode;
                selectedEditVendorNode = vendorNode;
                selectedEditTourNode = tourNode;

                var messageId = walkerNode?.WalkerData?.MessageId;
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    // await ToastService.ShowWarningAsync("Edit Not Available", "No booking information found for this walker.");
                    return;
                }

                showEditDialog = true;
                isEditLoading = true;
                StateHasChanged();

                try
                {
                    var catalogTask = EnsureCatalogAsync();
                    var vendorsTask = EnsureVendorsAsync();
                    var processedEmailTask = TourTreeService.GetProcessedEmailByMessageIdAsync(messageId);
                    await Task.WhenAll(catalogTask, vendorsTask, processedEmailTask);

                    var response = await processedEmailTask;
                    if (response != null)
                    {
                        selectedProcessedEmail = response;
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(selectedProcessedEmail);
                            selectedEditOriginalEmail = System.Text.Json.JsonSerializer.Deserialize<ProcessedEmail>(json);
                        }
                        catch
                        {
                            selectedEditOriginalEmail = null;
                        }
                        selectedProcessedEmail.TourName = TourCatalogService.NormalizeName(selectedProcessedEmail.TourName ?? string.Empty);
                        selectedProcessedEmail.TourTime = NormalizeTimeSafe(selectedProcessedEmail.TourTime);
                        editTimeOptions = GetTimeOptionsFor(selectedProcessedEmail.TourName);
                        if (string.IsNullOrWhiteSpace(selectedProcessedEmail.TourTime) && editTimeOptions.Count > 0)
                        {
                            selectedProcessedEmail.TourTime = editTimeOptions.First();
                        }
                        if (string.IsNullOrEmpty(selectedProcessedEmail.EmailType) ||
                            !new[] { "booking", "modification", "cancellation", "other" }.Contains(selectedProcessedEmail.EmailType))
                        {
                            selectedProcessedEmail.EmailType = "booking";
                        }
                        RefreshEditVendorOptions();
                        editReportIssueNotes = string.Empty;
                        isSubmittingEditIssue = false;
                    }
                    else
                    {
                        // await ToastService.ShowWarningAsync("Edit Not Available", "No processed email data found for this booking.");
                        CloseEditDialog();
                        return;
                    }
                }
                finally
                {
                    isEditLoading = false;
                    StateHasChanged();
                }
            }
            catch
            {
                showEditDialog = false;
                isEditLoading = false;
                StateHasChanged();
                // await ToastService.ShowErrorAsync("Error", "Failed to open edit dialog.");
            }
        }





        private string FormatPhoneToE164(string phoneNumber)
        {
            try
            {
                // Remove all non-digit characters except +
                var cleaned = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());

                // If it already starts with +, it's likely in E.164 format
                if (cleaned.StartsWith("+"))
                {
                    return cleaned;
                }

                // If it's a US number (10 digits), add +1
                if (cleaned.Length == 10)
                {
                    return "+1" + cleaned;
                }

                // If it's 11 digits and starts with 1, add +
                if (cleaned.Length == 11 && cleaned.StartsWith("1"))
                {
                    return "+" + cleaned;
                }

                // For other formats, assume it needs country code (simplified)
                if (cleaned.Length >= 10)
                {
                    // Assume US numbers if we can't determine
                    return "+1" + cleaned.Substring(Math.Max(0, cleaned.Length - 10));
                }

                return cleaned; // Return as-is if we can't format it
            }
            catch
            {
                return phoneNumber; // Return original if formatting fails
            }
        }

        // 2025-11-20 00:00 UTC - Helper (VERBATIM from Workstation; not used by shaped flow but included for parity)
        private (string Name, string Phone, int Attendees) ParseWalkerLabel(string walkerLabel)
        {
            try
            {
                var attendees = 0;
                var attendeesFound = false;
                var parts = walkerLabel.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 3)
                {
                    var phoneStartIndex = -1;
                    var attendeeTokens = new HashSet<int>();

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        // Support attendees as integer (e.g., "2") OR adults.children (e.g., "2.1", "2.5")
                        var token = parts[i];
                        var matchedAttendees = false;

                        if (int.TryParse(token, out var attendeeCount) && attendeeCount >= 1 && attendeeCount <= 10)
                        {
                            attendees = attendeeCount;
                            attendeesFound = true;
                            attendeeTokens.Add(i);
                            matchedAttendees = true;
                        }
                        else
                        {
                            var decMatch = System.Text.RegularExpressions.Regex.Match(token, @"^(\d{1,2})\.(\d{1,2})$");
                            if (decMatch.Success)
                            {
                                var adults = int.Parse(decMatch.Groups[1].Value);
                                var children = int.Parse(decMatch.Groups[2].Value);
                                attendees = Math.Max(0, adults) + Math.Max(0, children);
                                attendeesFound = true;
                                attendeeTokens.Add(i);
                                matchedAttendees = true;
                            }
                        }

                        if (matchedAttendees)
                        {
                            var nextPart = parts[i + 1];
                            if (nextPart.StartsWith("+") || (nextPart.Length >= 10 && nextPart.All(char.IsDigit)))
                            {
                                phoneStartIndex = i + 1;
                                break;
                            }
                        }
                    }

                    if (phoneStartIndex >= 0 && phoneStartIndex < parts.Length)
                    {
                        var name = string.Join(" ", parts.Take(phoneStartIndex - 1));
                        var phone = string.Join(" ", parts.Skip(phoneStartIndex));
                        return (name, phone, attendees);
                    }
                    else if (attendeesFound)
                    {
                        // 2025-12-10 00:00 UTC - Strip attendee tokens from name when no phone is present
                        var cleaned = parts
                            .Where((p, idx) =>
                            {
                                if (attendeeTokens.Contains(idx)) return false;
                                var isInt = int.TryParse(p, out var intVal) && intVal >= 1 && intVal <= 10;
                                var isDec = System.Text.RegularExpressions.Regex.IsMatch(p, @"^(\d{1,2})\.(\d{1,2})$");
                                return !(isInt || isDec);
                            })
                            .ToList();
                        var nameOnly = string.Join(" ", cleaned).Trim();
                        return (string.IsNullOrWhiteSpace(nameOnly) ? walkerLabel : nameOnly, "", attendees);
                    }
                }

                // Fallback: extract phone via regex even if attendees token pattern is not present
                var phoneMatch = System.Text.RegularExpressions.Regex.Match(
                    walkerLabel,
                    @"(\+?\d[\d\-\s\(\)]{9,})"
                );

                if (phoneMatch.Success)
                {
                    var phone = phoneMatch.Value.Trim();
                    var namePortion = walkerLabel.Substring(0, phoneMatch.Index).Trim();

                    // Try to extract attendees (1-10) from the name portion and remove it from the name
                    var attendeesInName = 0;

                    // First check adults.children pattern like 2.1 or 2.5
                    var decInName = System.Text.RegularExpressions.Regex.Match(namePortion, @"(?<!\d)(\d{1,2})\.(\d{1,2})(?!\d)");
                    if (decInName.Success)
                    {
                        var adults = int.Parse(decInName.Groups[1].Value);
                        var children = int.Parse(decInName.Groups[2].Value);
                        attendeesInName = Math.Max(0, adults) + Math.Max(0, children);
                        namePortion = namePortion.Remove(decInName.Index, decInName.Length).Trim();
                    }
                    else
                    {
                        // Fallback to simple integer attendees
                        var attendeesMatch = System.Text.RegularExpressions.Regex.Match(namePortion, @"(?<!\d)([1-9]|10)(?!\d)");
                        if (attendeesMatch.Success && int.TryParse(attendeesMatch.Groups[1].Value, out var att))
                        {
                            attendeesInName = att;
                            namePortion = namePortion.Remove(attendeesMatch.Index, attendeesMatch.Length).Trim();
                        }
                    }

                    var name = namePortion;
                    return (name, phone, attendeesInName);
                }

                // 2025-12-10 00:00 UTC - Preserve attendee counts even when phone is missing
                if (!attendeesFound)
                {
                    var decStandalone = System.Text.RegularExpressions.Regex.Match(walkerLabel, @"(?<!\d)(\d{1,2})\.(\d{1,2})(?!\d)");
                    if (decStandalone.Success)
                    {
                        var adults = int.Parse(decStandalone.Groups[1].Value);
                        var children = int.Parse(decStandalone.Groups[2].Value);
                        attendees = Math.Max(0, adults) + Math.Max(0, children);
                        attendeesFound = true;
                    }
                    else
                    {
                        var attendeesMatchStandalone = System.Text.RegularExpressions.Regex.Match(walkerLabel, @"(?<!\d)([1-9]|10)(?!\d)");
                        if (attendeesMatchStandalone.Success && int.TryParse(attendeesMatchStandalone.Groups[1].Value, out var attStandalone))
                        {
                            attendees = attStandalone;
                            attendeesFound = true;
                        }
                    }
                }

                return (walkerLabel, "", attendees);
            }
            catch
            {
                return (walkerLabel, "", 0);
            }
        }

        private void CloseEditDialog()
        {
            showEditDialog = false;
            isEditLoading = false;
            selectedProcessedEmail = null;
            selectedEditOriginalEmail = null;
            selectedEditWalkerNode = null;
            selectedEditVendorNode = null;
            selectedEditTourNode = null;
            editVendorOptions = new List<string>();
            editReportIssueNotes = string.Empty;
            isSubmittingEditIssue = false;
            StateHasChanged();
        }

        // 2025-11-21 00:00 UTC - Save edit changes (updates ProcessedEmail + Booking, then refreshes)
        // Modified: 2026-03-24 - Restored toast feedback; decoupled success from Bookings row match
        private async Task SaveEditChanges()
        {
            try
            {
                if (selectedProcessedEmail == null)
                {
                    await ToastService.ShowWarningAsync("No Data", "No booking data to save.");
                    return;
                }

                selectedProcessedEmail.NumberOfAttendees = Math.Max(0, selectedProcessedEmail.NumberOfAttendees ?? 0);
                selectedProcessedEmail.NumberOfAdults = Math.Max(0, selectedProcessedEmail.NumberOfAdults ?? 0);
                selectedProcessedEmail.NumberOfChildren = Math.Max(0, selectedProcessedEmail.NumberOfChildren ?? 0);
                selectedProcessedEmail.TourName = TourCatalogService.NormalizeName(selectedProcessedEmail.TourName ?? string.Empty);
                selectedProcessedEmail.TourTime = NormalizeTimeSafe(selectedProcessedEmail.TourTime);
                var normalizedEmailType = (selectedProcessedEmail.EmailType ?? string.Empty).Trim().ToLowerInvariant();
                if (!new[] { "booking", "modification", "cancellation", "other" }.Contains(normalizedEmailType))
                {
                    normalizedEmailType = "booking";
                }
                selectedProcessedEmail.EmailType = normalizedEmailType;

                if (string.IsNullOrWhiteSpace(selectedProcessedEmail.TourName))
                {
                    await ToastService.ShowWarningAsync("Validation", "Tour name is required.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(selectedProcessedEmail.TourTime))
                {
                    await ToastService.ShowWarningAsync("Validation", "Tour time is required.");
                    return;
                }

                var success = await TourTreeService.UpdateProcessedEmailAndBookingAsync(selectedProcessedEmail);
                if (success)
                {
                    CloseEditDialog();
                    if (OnDataRefresh.HasDelegate)
                    {
                        await OnDataRefresh.InvokeAsync();
                    }
                    await ToastService.ShowSuccessAsync("Changes Saved", "Booking details have been updated successfully.");
                }
                else
                {
                    await ToastService.ShowErrorAsync("Error", "Failed to save changes to database.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TourTreeDisplay] SaveEditChanges error: {ex.Message}");
                await ToastService.ShowErrorAsync("Save Error", "Failed to save changes. Please try again.");
            }
        }

    }

}

