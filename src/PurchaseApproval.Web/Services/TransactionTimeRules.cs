// Resolves the wall-clock date/time an executor typed into a precise UTC instant.
//
// Why this exists: a browser only knows its *current* offset. If the payment happened before
// a daylight-saving change, the offset at that moment differs, so the server resolves the
// entered time inside the device's IANA zone at that date. Two edge cases need explicit
// handling: a local time that never existed (clocks jumped forward) and a local time that
// occurred twice (clocks fell back). Related: TransferExecutionRules.ApplyOutcome.
namespace PurchaseApproval.Services;

/// <summary>What the executor submitted: the typed local time, the device zone and, when the
/// zone is unavailable or the time is ambiguous, an explicit UTC offset choice.</summary>
public sealed record TransactionTimeInput(
    DateTime EnteredLocalDateTime,
    string? TimeZoneId,
    short? ExplicitOffsetMinutes);

public enum TransactionTimeResolution
{
    Resolved = 1,
    MissingTimeZoneAndOffset = 2,
    InvalidTimeZone = 3,
    InvalidOffset = 4,
    NonexistentLocalTime = 5,
    AmbiguousLocalTime = 6,
    MissingDateTime = 7
}

public sealed record TransactionTimeResult(
    TransactionTimeResolution Status,
    DateTimeOffset? OccurrenceUtc,
    short? OffsetMinutes,
    string? TimeZoneId,
    DateTime EnteredLocalDateTime,
    IReadOnlyList<short> OffsetChoices)
{
    public bool IsResolved => Status == TransactionTimeResolution.Resolved;
}

public static class TransactionTimeRules
{
    // UTC offsets in the real world span -12:00 to +14:00.
    public const short MinimumOffsetMinutes = -840;
    public const short MaximumOffsetMinutes = 840;

    public static TransactionTimeResult Resolve(TransactionTimeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // The typed value has no inherent zone; Unspecified prevents .NET from guessing one.
        var local = DateTime.SpecifyKind(input.EnteredLocalDateTime, DateTimeKind.Unspecified);
        var zoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? null : input.TimeZoneId.Trim();

        // An empty form field binds to DateTime.MinValue; years outside this window cannot be
        // combined with any offset without overflowing DateTimeOffset, so reject them up front.
        if (local == default || local.Year is < 1900 or > 2200)
        {
            return Failure(TransactionTimeResolution.MissingDateTime, zoneId, local);
        }

        if (input.ExplicitOffsetMinutes is { } explicitOffset
            && explicitOffset is < MinimumOffsetMinutes or > MaximumOffsetMinutes)
        {
            return Failure(TransactionTimeResolution.InvalidOffset, zoneId, local);
        }

        if (zoneId is null)
        {
            // No device zone: the executor must have chosen an explicit offset.
            if (input.ExplicitOffsetMinutes is not { } offset)
            {
                return Failure(TransactionTimeResolution.MissingTimeZoneAndOffset, null, local);
            }

            return Resolved(local, offset, null);
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return Failure(TransactionTimeResolution.InvalidTimeZone, zoneId, local);
        }
        catch (InvalidTimeZoneException)
        {
            return Failure(TransactionTimeResolution.InvalidTimeZone, zoneId, local);
        }

        if (zone.IsInvalidTime(local))
        {
            return Failure(TransactionTimeResolution.NonexistentLocalTime, zone.Id, local);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var choices = zone.GetAmbiguousTimeOffsets(local)
                .Select(offset => checked((short)offset.TotalMinutes))
                .OrderByDescending(minutes => minutes)
                .ToList();

            if (input.ExplicitOffsetMinutes is { } chosen && choices.Contains(chosen))
            {
                return Resolved(local, chosen, zone.Id);
            }

            return new TransactionTimeResult(
                TransactionTimeResolution.AmbiguousLocalTime, null, null, zone.Id, local, choices);
        }

        var resolvedOffset = checked((short)zone.GetUtcOffset(local).TotalMinutes);
        return Resolved(local, resolvedOffset, zone.Id);
    }

    private static TransactionTimeResult Resolved(DateTime local, short offsetMinutes, string? zoneId)
    {
        var occurrence = new DateTimeOffset(local, TimeSpan.FromMinutes(offsetMinutes)).ToUniversalTime();
        return new TransactionTimeResult(
            TransactionTimeResolution.Resolved, occurrence, offsetMinutes, zoneId, local, []);
    }

    private static TransactionTimeResult Failure(
        TransactionTimeResolution status,
        string? zoneId,
        DateTime local)
        => new(status, null, null, zoneId, local, []);

    /// <summary>Human-readable explanation for a failed resolution, used as a localization key.</summary>
    public static LocalizableMessage Describe(TransactionTimeResult result) => result.Status switch
    {
        TransactionTimeResolution.MissingDateTime =>
            "Enter when the transfer or attempt occurred.",
        TransactionTimeResolution.MissingTimeZoneAndOffset =>
            "Choose the timezone or UTC offset that applied when the transaction occurred.",
        TransactionTimeResolution.InvalidTimeZone =>
            new LocalizableMessage("The timezone '{0}' is not recognized. Choose a valid IANA timezone.", result.TimeZoneId ?? string.Empty),
        TransactionTimeResolution.InvalidOffset =>
            "The reported timezone offset is invalid.",
        TransactionTimeResolution.NonexistentLocalTime =>
            "That local time did not exist because clocks moved forward on that date. Enter the corrected time.",
        TransactionTimeResolution.AmbiguousLocalTime =>
            "That local time occurred twice because clocks moved back on that date. Choose which UTC offset applied.",
        _ => "The transaction time could not be resolved."
    };

    /// <summary>Formats an offset such as +03:30 for display and for ambiguity choices.</summary>
    public static string FormatOffset(short offsetMinutes)
    {
        var span = TimeSpan.FromMinutes(Math.Abs(offsetMinutes));
        return $"{(offsetMinutes < 0 ? "-" : "+")}{(int)span.TotalHours:00}:{span.Minutes:00}";
    }
}
