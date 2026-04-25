// 2025-12-11 00:00 UTC - Console logger for walker selection output
using System;
using System.Linq;
using System.Text;
using Email.TourTreeViewShapedData.Models;

namespace Email.Components.Tours
{
    internal static class WalkerSelectionConsoleLogger
    {
        // 2025-12-11 00:00 UTC - Write walker selection details to .NET console (not JS)
        public static void LogSelection(ShapedTreeData? tourData, ShapedTreeNode walkerNode)
        {
            try
            {
                var ctx = ResolveWalkerContext(tourData, walkerNode);
                var w = walkerNode?.WalkerData;

                var sb = new StringBuilder();
                sb.AppendLine("START NODE SELECTION");
                sb.AppendLine("---------------------");
                sb.AppendLine($"Date: {OrNone(ctx.DateLabel)}");
                sb.AppendLine($"Tour: {OrNone(ctx.TourLabel)}");
                sb.AppendLine($"Vendor: {OrNone(ctx.VendorLabel)}");
                sb.AppendLine($"Walker Name: {OrNone(w?.DisplayName)}");
                sb.AppendLine($"Walker Phone: {OrNone(w?.DisplayPhone)}");
                sb.AppendLine($"Walker Attendees: {(w?.Attendees.ToString() ?? "NONE")}");
                sb.AppendLine($"Tour Date: {OrNone(w?.TourDate)}");
                sb.AppendLine($"Tour Time: {OrNone(w?.TourTime)}");
                sb.AppendLine($"Meeting Place: {OrNone(w?.MeetingPlace)}");
                sb.AppendLine($"Meeting Time: {OrNone(w?.MeetingTime)}");
                sb.AppendLine($"Meeting Instructions: {OrNone(w?.MeetingInstructions)}");
                sb.AppendLine($"Tour Start Time: {OrNone(w?.TourStartTime)}");
                sb.AppendLine($"Tour Location: {OrNone(w?.TourLocation)}");
                sb.AppendLine($"Vendor Name: {OrNone(w?.VendorName)}");
                sb.AppendLine($"Status: {OrNone(w?.Status)}");
                sb.AppendLine($"Email Type: {OrNone(w?.EmailType)}");
                sb.AppendLine($"Language: {OrNone(w?.Language)}");
                sb.AppendLine($"Booking Code: {OrNone(w?.BookingCode)}");
                sb.AppendLine($"MessageId: {OrNone(w?.MessageId)}");
                sb.AppendLine($"Message Sent: {(w?.MessageSent == true ? "true" : "false")}");
                sb.AppendLine($"Message Sent At (UTC): {w?.MessageSentAtUtc?.ToString("o") ?? "NONE"}");
                sb.AppendLine("---------------------");
                sb.AppendLine("END NODE SELECTION");
                sb.AppendLine();
                sb.AppendLine("START MESSAGE DETAILS");
                sb.AppendLine("----------------------------");
                if (w?.MessageStages != null && w.MessageStages.Count > 0)
                {
                    foreach (var kvp in w.MessageStages.OrderBy(x => x.Key))
                    {
                        var st = kvp.Value;
                        sb.AppendLine($"Stage: {kvp.Key} | Sent: {(st?.SentFlag == true ? "true" : "false")} | At (UTC): {st?.SentAtUtc?.ToString("o") ?? "NONE"} | Channel: {OrNone(st?.Channel)} | TemplateId: {(st?.TemplateId?.ToString() ?? "NONE")}");
                    }
                }
                else
                {
                    sb.AppendLine("Message Stages: NONE");
                }
                sb.AppendLine("----------------------------");
                sb.AppendLine("END MESSAGE DETAILS");

                Console.WriteLine(sb.ToString());
            }
            catch
            {
                // Swallow logging errors to avoid blocking selection
            }
        }

        // 2025-12-11 00:00 UTC - Resolve date/tour/vendor labels for a walker
        private static (string? DateLabel, string? TourLabel, string? VendorLabel) ResolveWalkerContext(ShapedTreeData? tourData, ShapedTreeNode walkerNode)
        {
            if (walkerNode == null || tourData?.TreeNodes == null) return (null, null, null);

            foreach (var dateNode in tourData.TreeNodes)
            {
                if (dateNode?.Children == null) continue;
                foreach (var tourNode in dateNode.Children)
                {
                    if (tourNode?.Children == null) continue;
                    foreach (var vendorNode in tourNode.Children)
                    {
                        if (vendorNode?.Children == null) continue;
                        if (vendorNode.Children.Any(child => ReferenceEquals(child, walkerNode)))
                        {
                            return (dateNode.Label, tourNode.Label, vendorNode.Label);
                        }
                    }
                }
            }

            return (null, null, null);
        }

        private static string OrNone(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "NONE" : value;
        }
    }
}

