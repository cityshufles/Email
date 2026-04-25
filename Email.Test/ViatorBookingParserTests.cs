using System.Globalization;
using System.Text.RegularExpressions;
using Email.Services.GmailProcessing.VendorParsers;

namespace Email.Test;

[TestFixture]
public class ViatorBookingParserTests
{
    private static string FixturePath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fileName);

    [Test]
    public void Parse_FullBookingText_ExtractsCoreFields()
    {
        var parser = new ViatorBookingParser();
        var subject = "Viator New Booking for Sun, Feb 22, 2026 (#BR-1361381631)";
        var text = File.ReadAllText(FixturePath("viator_booking_full_text.txt"));

        var result = parser.Parse(subject, htmlBody: null, textBody: text);

        Assert.That(result.VendorName, Is.EqualTo("Viator"));
        Assert.That(result.EmailType, Is.EqualTo("Booking"));
        Assert.That(result.Data.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.Data.CustomerName, Is.EqualTo("Christopher Knisely"));
        Assert.That(result.Data.CustomerEmail, Is.EqualTo("traveler@example.com"));
        Assert.That(result.Data.CustomerPhone, Does.Contain("5134486530"));
        Assert.That(result.Data.TourName, Is.EqualTo("Grand Central Terminal History and Mysteries"));
        Assert.That(result.Data.TourDate?.ToString("yyyy-MM-dd"), Is.EqualTo("2026-02-22"));
        Assert.That(result.Data.TourTime, Is.EqualTo("14:00"));
        Assert.That(result.Data.NumberOfAttendees, Is.EqualTo(2));
        Assert.That(result.Data.NumberOfAdults, Is.EqualTo(2));
    }

    [Test]
    public void Parse_FwdSubjectVariant_StillExtractsBookingCodeAndDate()
    {
        var parser = new ViatorBookingParser();
        var subject = "Fwd: Viator New Booking for Sun, Feb 22, 2026 (#BR-1361381631)";
        var text = File.ReadAllText(FixturePath("viator_booking_full_text.txt"));
        var html = File.ReadAllText(FixturePath("viator_booking_full_html.html"));

        var result = parser.Parse(subject, htmlBody: html, textBody: text);

        Assert.That(result.Data.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.Data.TourDate.HasValue, Is.True);
        Assert.That(result.Data.TourDate!.Value.DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
        Assert.That(result.Data.TourDate!.Value, Is.EqualTo(DateTime.Parse("2026-02-22", CultureInfo.InvariantCulture)));
    }

    [Test]
    public void Parse_MinimalCheckfrontNotification_ExtractsFullWalkerName()
    {
        var parser = new ViatorBookingParser();
        var subject = "Viator booking notification";
        var text = "KATIA YAZMIN CAVAZOS LEAL viator/gyg booking";

        var result = parser.Parse(subject, htmlBody: null, textBody: text);

        Assert.That(result.Data.CustomerName, Is.EqualTo("KATIA YAZMIN CAVAZOS LEAL"));
    }

    [Test]
    public void RawFixture_BasicTextExtraction_ExtractsHtmlPayload()
    {
        var raw = File.ReadAllText(FixturePath("viatourfullbooking.txt"));
        var htmlBody = ExtractHtmlPayload(raw);

        Assert.That(htmlBody, Is.Not.Empty);
        Assert.That(htmlBody, Does.Contain("Booking Reference:"));
        Assert.That(htmlBody, Does.Contain("Lead Traveler Name:"));
        Assert.That(htmlBody, Does.Contain("Travelers:"));
    }

    [Test]
    public void Parse_RealFlowSubjectAndHtmlPayload_ExtractsExpectedValues()
    {
        var parser = new ViatorBookingParser();
        var subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)";
        var htmlBody = ExtractHtmlPayload(File.ReadAllText(FixturePath("viatourfullbooking.txt")));

        var result = parser.Parse(subject, htmlBody, textBody: null);

        Assert.That(result.VendorName, Is.EqualTo("Viator"));
        Assert.That(result.EmailType, Is.EqualTo("Booking"));
        Assert.That(result.Data.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.Data.CustomerName, Is.EqualTo("Christopher Knisely"));
        Assert.That(result.Data.CustomerPhone, Does.Contain("5134486530"));
        Assert.That(result.Data.TourName, Is.EqualTo("Grand Central Terminal History and Mysteries"));
        Assert.That(result.Data.TourDate?.ToString("yyyy-MM-dd"), Is.EqualTo("2026-02-22"));
        Assert.That(result.Data.TourTime, Is.EqualTo("14:00"));
        Assert.That(result.Data.NumberOfAdults, Is.EqualTo(2));
        Assert.That(result.Data.NumberOfAttendees, Is.EqualTo(2));
        Assert.That(result.Data.Language, Does.Contain("English"));
    }

    [Test]
    public void Parse_RealFlowFwdSubjectAndHtmlPayload_ExtractsExpectedValues()
    {
        var parser = new ViatorBookingParser();
        var subject = "Fwd: New Booking for Sun, Feb 22, 2026 (#BR-1361381631)";
        var htmlBody = ExtractHtmlPayload(File.ReadAllText(FixturePath("viatourfullbooking.txt")));

        var result = parser.Parse(subject, htmlBody, textBody: null);

        Assert.That(result.Data.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.Data.CustomerName, Is.EqualTo("Christopher Knisely"));
        Assert.That(result.Data.TourDate?.ToString("yyyy-MM-dd"), Is.EqualTo("2026-02-22"));
        Assert.That(result.Data.TourTime, Is.EqualTo("14:00"));
        Assert.That(result.Data.NumberOfAdults, Is.EqualTo(2));
    }

    [Test]
    public void ParseV2_RealFlowHtmlPayload_ExtractsExtendedBookingDetailsModel()
    {
        var parser = new ViatorBookingParserV2();
        var subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)";
        var htmlBody = ExtractHtmlPayload(File.ReadAllText(FixturePath("viatourfullbooking.txt")));

        var result = parser.Parse(subject, htmlBody, textBody: null);
        var details = result.Data.ViatorDetailsV2;

        Assert.That(details, Is.Not.Null);
        Assert.That(details!.BookingReference, Is.EqualTo("BR-1361381631"));
        Assert.That(details.TourName, Is.EqualTo("Grand Central Terminal History and Mysteries"));
        Assert.That(details.TravelDate?.ToString("yyyy-MM-dd"), Is.EqualTo("2026-02-22"));
        Assert.That(details.LeadTravelerName, Is.EqualTo("Christopher Knisely"));
        Assert.That(details.TravelerNames, Does.Contain("Passenger Two"));
        Assert.That(details.TravelersRaw, Is.EqualTo("2 Adults"));
        Assert.That(details.TravelersAdults, Is.EqualTo(2));
        Assert.That(details.TravelersTotal, Is.EqualTo(2));
        Assert.That(details.ProductCode, Is.EqualTo("5527066P31"));
        Assert.That(details.TourGrade, Does.Contain("14:00"));
        Assert.That(details.TourGradeCode, Is.EqualTo("TG1~14:00"));
        Assert.That(details.TourGradeDescription, Is.EqualTo("Grand Central Terminal History and Mysteries"));
        Assert.That(details.TourLanguage, Is.EqualTo("English - Guide"));
        Assert.That(details.Location, Is.EqualTo("New York, United States"));
        Assert.That(details.NetRateCurrency, Is.EqualTo("USD"));
        Assert.That(details.NetRateAmount, Is.EqualTo(58.64m));
        Assert.That(details.MeetingPoint, Does.Contain("Grand Central Terminal"));
        Assert.That(details.SpecialRequirements, Is.EqualTo("No"));
        Assert.That(details.PhoneNormalized, Does.Contain("5134486530"));
        Assert.That(details.OptionalText, Does.Contain("Acknowledge this booking for your records."));
    }

    [Test]
    public void Parse_DbFixtureHtmlPayload_ExtractsCanonicalBookingFields()
    {
        var parser = new ViatorBookingParserV2();
        var raw = File.ReadAllText(FixturePath("viatordb booking.txt"));
        var htmlStart = raw.IndexOf("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase);
        Assert.That(htmlStart, Is.GreaterThanOrEqualTo(0), "Expected HTML payload in DB fixture.");

        var htmlBody = raw[htmlStart..];
        var subject = "New Booking for Sun, Feb 22, 2026 (#BR-1361381631)";

        var result = parser.Parse(subject, htmlBody, textBody: null);

        Assert.That(result.VendorName, Is.EqualTo("Viator"));
        Assert.That(result.EmailType, Is.EqualTo("Booking"));
        Assert.That(result.Data.BookingCode, Is.EqualTo("BR-1361381631"));
        Assert.That(result.Data.TourDate?.ToString("yyyy-MM-dd"), Is.EqualTo("2026-02-22"));
        Assert.That(result.Data.TourTime, Is.EqualTo("14:00"), "Canonical time must remain HH:mm.");
    }

    private static string ExtractHtmlPayload(string raw)
    {
        var delimiterMatch = Regex.Match(raw, @"(?m)^\s*-{6,}\s*$");
        var content = delimiterMatch.Success
            ? raw[(delimiterMatch.Index + delimiterMatch.Length)..]
            : raw;

        var htmlStart = Regex.Match(content, @"<\s*(html|body|div|table)\b", RegexOptions.IgnoreCase);
        if (htmlStart.Success)
        {
            return content[htmlStart.Index..].Trim();
        }

        return content.Trim();
    }
}
