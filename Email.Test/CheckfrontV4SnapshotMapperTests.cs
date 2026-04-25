using Email.Models;
using Email.Services;

namespace Email.Test;

[TestFixture]
public class CheckfrontV4SnapshotMapperTests
{
    [Test]
    public void TryMapToLegacyBooking_ValidSnapshot_MapsCoreFields()
    {
        var mapper = new CheckfrontV4SnapshotMapper();
        var snapshot = new DbCheckfrontV4Booking
        {
            Id = 1,
            CheckfrontBookingId = "9001",
            BookingCode = "BR-9001",
            SnapshotJson = @"{
  ""id"": ""9001"",
  ""code"": ""BR-9001"",
  ""created"": ""2026-04-01T12:00:00-04:00"",
  ""start"": ""2026-04-15T09:30:00-04:00"",
  ""checkIn"": ""2026-04-15T09:15:00-04:00"",
  ""firstName"": ""Jamie"",
  ""lastName"": ""Rivera"",
  ""email"": ""jamie.rivera@example.com"",
  ""statusId"": ""CONFIRMED"",
  ""status"": { ""id"": ""CONFIRMED"", ""name"": ""Confirmed"" },
  ""itemSummary"": ""Brooklyn Bridge Tour"",
  ""fields"": {
    ""customer_phone"": ""+1 (555) 123-4567"",
    ""adults"": ""2"",
    ""children"": ""1""
  }
}"
        };

        var ok = mapper.TryMapToLegacyBooking(snapshot, out var booking, out var reason);

        Assert.That(ok, Is.True, reason);
        Assert.That(booking.Code, Is.EqualTo("BR-9001"));
        Assert.That(booking.CustomerName, Is.EqualTo("Jamie Rivera"));
        Assert.That(booking.CustomerEmail, Is.EqualTo("jamie.rivera@example.com"));
        Assert.That(booking.CustomerPhone, Is.EqualTo("+1 (555) 123-4567"));
        Assert.That(booking.ItemName, Is.EqualTo("Brooklyn Bridge Tour"));
        Assert.That(booking.StartDateRaw, Is.EqualTo("2026-04-15"));
        Assert.That(booking.StartTimeRaw, Is.EqualTo("09:30"));
        Assert.That(booking.NumberOfAdults, Is.EqualTo(2));
        Assert.That(booking.NumberOfChildren, Is.EqualTo(1));
        Assert.That(booking.NumberOfAttendees, Is.EqualTo(3));
        Assert.That(booking.StatusName, Is.EqualTo("Confirmed"));
    }

    [Test]
    public void TryMapToLegacyBooking_MissingBookingCode_ReturnsFalse()
    {
        var mapper = new CheckfrontV4SnapshotMapper();
        var snapshot = new DbCheckfrontV4Booking
        {
            Id = 2,
            CheckfrontBookingId = string.Empty,
            BookingCode = string.Empty,
            SnapshotJson = @"{
  ""id"": """",
  ""code"": """",
  ""itemSummary"": ""Brooklyn Bridge Tour"",
  ""start"": ""2026-04-15T09:30:00-04:00""
}"
        };

        var ok = mapper.TryMapToLegacyBooking(snapshot, out var _, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(reason, Does.Contain("booking code").IgnoreCase);
    }

    [Test]
    public void TryMapToLegacyBooking_UsesNestedCustomerFallbackWhenTopLevelNameMissing()
    {
        var mapper = new CheckfrontV4SnapshotMapper();
        var snapshot = new DbCheckfrontV4Booking
        {
            Id = 3,
            CheckfrontBookingId = "9003",
            BookingCode = "BR-9003",
            SnapshotJson = @"{
  ""id"": ""9003"",
  ""code"": ""BR-9003"",
  ""created"": ""2026-04-01T12:00:00-04:00"",
  ""start"": ""2026-04-15T11:00:00-04:00"",
  ""firstName"": """",
  ""lastName"": """",
  ""email"": """",
  ""statusId"": ""CONFIRMED"",
  ""status"": { ""id"": ""CONFIRMED"", ""name"": ""Confirmed"" },
  ""itemSummary"": ""Lower Manhattan Tour"",
  ""customer"": {
    ""id"": ""7"",
    ""code"": ""CUST-007"",
    ""firstName"": ""Doris"",
    ""lastName"": ""Harrer-Dulnig"",
    ""email"": ""doris1977@gmail.com"",
    ""fields"": {
      ""customer_phone"": ""+1 312 555 0101""
    }
  }
}"
        };

        var ok = mapper.TryMapToLegacyBooking(snapshot, out var booking, out var reason);

        Assert.That(ok, Is.True, reason);
        Assert.That(booking.CustomerName, Is.EqualTo("Doris Harrer-Dulnig"));
        Assert.That(booking.CustomerEmail, Is.EqualTo("doris1977@gmail.com"));
        Assert.That(booking.CustomerPhone, Is.EqualTo("+1 312 555 0101"));
        Assert.That(booking.StartDateRaw, Is.EqualTo("2026-04-15"));
        Assert.That(booking.StartTimeRaw, Is.EqualTo("11:00"));
    }

    [Test]
    public void TryMapToLegacyBooking_BlankItemSummary_UsesItemsArrayFallback()
    {
        var mapper = new CheckfrontV4SnapshotMapper();
        var snapshot = new DbCheckfrontV4Booking
        {
            Id = 4,
            CheckfrontBookingId = "9004",
            BookingCode = "BR-9004",
            SnapshotJson = @"{
  ""id"": ""9004"",
  ""code"": ""BR-9004"",
  ""start"": ""2026-04-16T10:00:00-04:00"",
  ""firstName"": ""Taylor"",
  ""lastName"": ""Stone"",
  ""itemSummary"": """",
  ""items"": [
    { ""name"": ""Grand Central Terminal Tour"" }
  ]
}"
        };

        var ok = mapper.TryMapToLegacyBooking(snapshot, out var booking, out var reason);

        Assert.That(ok, Is.True, reason);
        Assert.That(booking.ItemName, Is.EqualTo("Grand Central Terminal Tour"));
        Assert.That(booking.ItemTitle, Is.EqualTo("Grand Central Terminal Tour"));
        Assert.That(booking.StartDateRaw, Is.EqualTo("2026-04-16"));
        Assert.That(booking.StartTimeRaw, Is.EqualTo("10:00"));
    }

    [Test]
    public void TryMapToLegacyBooking_BlankItemSummaryWithoutFallback_ReturnsFalse()
    {
        var mapper = new CheckfrontV4SnapshotMapper();
        var snapshot = new DbCheckfrontV4Booking
        {
            Id = 5,
            CheckfrontBookingId = "9005",
            BookingCode = "BR-9005",
            SnapshotJson = @"{
  ""id"": ""9005"",
  ""code"": ""BR-9005"",
  ""start"": ""2026-04-16T10:00:00-04:00"",
  ""itemSummary"": """",
  ""fields"": {
    ""customer_name"": ""Dominik Meisser""
  }
}"
        };

        var ok = mapper.TryMapToLegacyBooking(snapshot, out var _, out var reason);

        Assert.That(ok, Is.False);
        Assert.That(reason, Does.Contain("tour name/item summary").IgnoreCase);
    }
}
