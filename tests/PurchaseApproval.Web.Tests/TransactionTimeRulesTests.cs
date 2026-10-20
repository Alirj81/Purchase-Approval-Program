// Verifies occurrence-time resolution: zone-based offsets at the entered date, explicit offsets,
// invalid input, and the two daylight-saving edge cases.
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class TransactionTimeRulesTests
{
    [Fact]
    public void ZoneOffsetIsTakenAtTheEnteredDate_NotToday()
    {
        var summer = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 7, 15, 12, 0, 0), "Europe/Berlin", null));
        var winter = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 1, 15, 12, 0, 0), "Europe/Berlin", null));

        Assert.True(summer.IsResolved);
        Assert.True(winter.IsResolved);
        Assert.Equal((short)120, summer.OffsetMinutes);
        Assert.Equal((short)60, winter.OffsetMinutes);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero), summer.OccurrenceUtc);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero), winter.OccurrenceUtc);
    }

    [Fact]
    public void ExplicitOffsetWithoutZone_IsAccepted()
    {
        var result = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 30, 0), null, 210));

        Assert.True(result.IsResolved);
        Assert.Null(result.TimeZoneId);
        Assert.Equal((short)210, result.OffsetMinutes);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero), result.OccurrenceUtc);
    }

    [Fact]
    public void MissingZoneAndOffset_IsReported()
    {
        var result = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 30, 0), " ", null));

        Assert.Equal(TransactionTimeResolution.MissingTimeZoneAndOffset, result.Status);
    }

    [Theory]
    [InlineData(-841)]
    [InlineData(841)]
    public void OffsetOutsideRealWorldRange_IsRejected(short offset)
    {
        var result = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 30, 0), null, offset));

        Assert.Equal(TransactionTimeResolution.InvalidOffset, result.Status);
    }

    [Fact]
    public void UnknownZone_IsRejected()
    {
        var result = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 30, 0), "Mars/Olympus_Mons", null));

        Assert.Equal(TransactionTimeResolution.InvalidTimeZone, result.Status);
    }

    [Fact]
    public void SpringForwardGap_IsNonexistent()
    {
        var result = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 3, 29, 2, 30, 0), "Europe/Berlin", null));

        Assert.Equal(TransactionTimeResolution.NonexistentLocalTime, result.Status);
    }

    [Fact]
    public void FallBackOverlap_OffersBothOffsetsAndAcceptsOnlyThose()
    {
        var ambiguous = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 25, 2, 30, 0), "Europe/Berlin", null));

        Assert.Equal(TransactionTimeResolution.AmbiguousLocalTime, ambiguous.Status);
        Assert.Equal(new short[] { 120, 60 }, ambiguous.OffsetChoices);

        var chosenSummer = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 25, 2, 30, 0), "Europe/Berlin", 120));
        Assert.True(chosenSummer.IsResolved);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), chosenSummer.OccurrenceUtc);

        var wrongChoice = TransactionTimeRules.Resolve(
            new TransactionTimeInput(new DateTime(2026, 10, 25, 2, 30, 0), "Europe/Berlin", 210));
        Assert.Equal(TransactionTimeResolution.AmbiguousLocalTime, wrongChoice.Status);
    }

    [Theory]
    [InlineData(210, "+03:30")]
    [InlineData(-330, "-05:30")]
    [InlineData(0, "+00:00")]
    public void FormatOffset_ProducesSignedHoursAndMinutes(short minutes, string expected)
    {
        Assert.Equal(expected, TransactionTimeRules.FormatOffset(minutes));
    }
}
