// Chooses the UI language from one small cookie and nothing else.
//
// Why a custom provider: the built-in cookie provider also switches the *formatting* culture,
// which would turn amounts into Persian digits and dates into the Jalali calendar. The business
// rule is Western digits and Gregorian dates regardless of language, so this provider pins the
// formatting culture to "en" and only varies the UI culture between "fa" (default) and "en".
// Accept-Language is deliberately ignored so the default stays predictable for every visitor.
using Microsoft.AspNetCore.Localization;

namespace PurchaseApproval.Web.Localization;

public sealed class LanguageCookieRequestCultureProvider : RequestCultureProvider
{
    public const string CookieName = "PurchaseApproval.Language";
    public const string DefaultUiCulture = "fa";
    public const string FormattingCulture = "en";
    public static readonly string[] SupportedUiCultures = ["fa", "en"];

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var requested = httpContext.Request.Cookies[CookieName];
        var uiCulture = SupportedUiCultures.Contains(requested, StringComparer.OrdinalIgnoreCase)
            ? requested!.ToLowerInvariant()
            : DefaultUiCulture;

        return Task.FromResult<ProviderCultureResult?>(
            new ProviderCultureResult(FormattingCulture, uiCulture));
    }

    public static bool IsSupported(string? code)
        => SupportedUiCultures.Contains(code, StringComparer.OrdinalIgnoreCase);
}
