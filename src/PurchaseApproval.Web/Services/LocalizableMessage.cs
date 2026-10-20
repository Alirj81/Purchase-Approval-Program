// Validation messages produced by the business-rule classes are English sentences that double
// as localization keys. Keeping the format string and its arguments separate lets Razor Pages
// translate them (IStringLocalizer["{key}", args]) while unit tests and logs still read the
// formatted English text through ToString().
using System.Globalization;

namespace PurchaseApproval.Services;

public sealed record LocalizableMessage(string Key, params object[] Args)
{
    public override string ToString() => Args.Length == 0
        ? Key
        : string.Format(CultureInfo.InvariantCulture, Key, Args);

    public static implicit operator LocalizableMessage(string key) => new(key);
}

/// <summary>Base class for rule violations that pages may show verbatim to the user.</summary>
public abstract class ValidationMessagesException : Exception
{
    protected ValidationMessagesException(string summary, IReadOnlyList<LocalizableMessage> messages)
        : base(summary)
    {
        Messages = messages;
    }

    public IReadOnlyList<LocalizableMessage> Messages { get; }

    /// <summary>Formatted English text, kept for tests, logs and callers that do not localize.</summary>
    public IReadOnlyList<string> Errors => Messages.Select(message => message.ToString()).ToList();
}
