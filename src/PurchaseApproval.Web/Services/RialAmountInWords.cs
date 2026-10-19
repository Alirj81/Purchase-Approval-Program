// Converts the saved integer rial total into the English wording required by the approved payment form.
// This is display-only reporting logic: it never recalculates or changes the authoritative numeric total.
namespace PurchaseApproval.Services;

internal static class RialAmountInWords
{
    private static readonly string[] BelowTwenty =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
        "seventeen", "eighteen", "nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    ];

    public static string Format(long amountRials)
    {
        if (amountRials < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountRials),
                amountRials,
                "A rial amount cannot be negative.");
        }

        var words = ConvertNumber(amountRials);
        var sentenceCase = char.ToUpperInvariant(words[0]) + words[1..];
        var currency = amountRials == 1 ? "Iranian rial" : "Iranian rials";
        return $"{sentenceCase} {currency} only";
    }

    private static string ConvertNumber(long value)
    {
        if (value < 20)
        {
            return BelowTwenty[(int)value];
        }

        if (value < 100)
        {
            var remainder = value % 10;
            return remainder == 0
                ? Tens[(int)(value / 10)]
                : $"{Tens[(int)(value / 10)]}-{BelowTwenty[(int)remainder]}";
        }

        if (value < 1_000)
        {
            return JoinScale(value, 100, "hundred");
        }

        if (value < 1_000_000)
        {
            return JoinScale(value, 1_000, "thousand");
        }

        if (value < 1_000_000_000)
        {
            return JoinScale(value, 1_000_000, "million");
        }

        return JoinScale(value, 1_000_000_000, "billion");
    }

    private static string JoinScale(long value, long scale, string scaleName)
    {
        var major = ConvertNumber(value / scale);
        var remainder = value % scale;
        return remainder == 0
            ? $"{major} {scaleName}"
            : $"{major} {scaleName} {ConvertNumber(remainder)}";
    }
}
