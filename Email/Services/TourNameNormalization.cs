// Created: 2026-02-11 14:50 UTC
// Purpose: String normalization utility for tour names - NO dictionaries, NO lookups
// This class ONLY cleans and normalizes strings for comparison purposes

using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Email.Services
{
	public static class TourNameNormalization
	{
		/// <summary>
		/// Normalize a tour name for grouping/comparison purposes
		/// This ONLY performs string cleaning - NO dictionary lookups or mappings
		/// Created: 2025-11-23 00:00 UTC
		/// Modified: 2026-02-11 - Removed all hardcoded aliases, pure string normalization only
		/// </summary>
		/// <param name="vendorName">Vendor name (not used in normalization, kept for signature compatibility)</param>
		/// <param name="rawTourName">Raw tour name to normalize</param>
		/// <returns>Normalized string suitable for comparison</returns>
		public static string NormalizeTourNameForGrouping(string vendorName, string rawTourName)
		{
			if (string.IsNullOrWhiteSpace(rawTourName)) return string.Empty;

			// 1. HTML decode
			var decoded = WebUtility.HtmlDecode(rawTourName).Trim();
			
			// 2. Remove common suffix noise
			// 2026-03-14: Handle duplicated vendor suffix variant "... Tour Tour Reservation"
			decoded = Regex.Replace(decoded, @"\bTour\s+Tour\s+Reservation\b", string.Empty, RegexOptions.IgnoreCase).Trim();
			decoded = Regex.Replace(decoded, @"\bTour Reservation\b", string.Empty, RegexOptions.IgnoreCase).Trim();

			// 3. Lowercase
			var lowered = decoded.ToLowerInvariant();

			// 4. Replace any non a-z0-9 with space
			var lettersDigitsOnly = Regex.Replace(lowered, "[^a-z0-9]+", " ");

			// 5. Collapse newlines and multiple spaces
			var compact = Regex.Replace(lettersDigitsOnly, @"\n|\r", " ");
			compact = Regex.Replace(compact, @"\s+", " ").Trim();

			return compact;
		}

		/// <summary>
		/// Normalize a tour name and return in Title Case for display
		/// Use this when you need a cleaned, human-readable version without DB lookup
		/// </summary>
		public static string NormalizeTourNameForDisplay(string rawTourName)
		{
			var normalized = NormalizeTourNameForGrouping(string.Empty, rawTourName);
			if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
			
			return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(normalized);
		}
	}
}
