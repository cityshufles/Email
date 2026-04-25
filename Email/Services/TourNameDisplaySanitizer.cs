using System.Net;
using System.Text.RegularExpressions;

namespace Email.Services
{
    /// <summary>
    /// Shared display sanitizer for tour names used in public/user-facing text.
    /// Keeps original wording while collapsing common duplicated Tour suffix variants.
    /// </summary>
    public static class TourNameDisplaySanitizer
    {
        public static string Normalize(string? tourName)
        {
            if (string.IsNullOrWhiteSpace(tourName))
            {
                return string.Empty;
            }

            var cleaned = WebUtility.HtmlDecode(tourName).Trim();

            cleaned = Regex.Replace(
                cleaned,
                @"\bTour\s+Tour\s+Reservation\b",
                "Tour",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = Regex.Replace(
                cleaned,
                @"\bTour\s+Reservation\b",
                "Tour",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = Regex.Replace(
                cleaned,
                @"\bTour\s+Tour\b",
                "Tour",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            cleaned = Regex.Replace(
                cleaned,
                @"\s{2,}",
                " ",
                RegexOptions.CultureInvariant);

            return cleaned.Trim();
        }
    }
}
