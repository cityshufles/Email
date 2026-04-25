using System.Globalization;
using PhoneNumbers;

namespace Email.Services.GmailProcessing.Extraction
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Extracts country from phone numbers (will use libphonenumber in implementation step).
    /// </summary>
    public static class CountryExtractor
    {
        public static bool TryGetCountryNameFromPhone(string? e164Phone, out string? countryName)
        {
            countryName = null;
            if (string.IsNullOrWhiteSpace(e164Phone))
            {
                return false;
            }

            try
            {
                var util = PhoneNumberUtil.GetInstance();
                var number = util.Parse(e164Phone, null);
                var regionCode = util.GetRegionCodeForNumber(number);
                if (!string.IsNullOrWhiteSpace(regionCode))
                {
                    try
                    {
                        var ri = new RegionInfo(regionCode);
                        countryName = ri.EnglishName;
                        return true;
                    }
                    catch
                    {
                        // RegionInfo may throw for unknown regions; fall through
                    }
                }
            }
            catch
            {
                // fall through
            }

            return false;
        }
    }
}


