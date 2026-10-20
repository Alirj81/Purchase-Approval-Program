// Stores the chosen UI language in a cookie and returns to the page the switch was pressed on.
// POST-only (anti-forgery protected) so a crafted link cannot change a visitor's language.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PurchaseApproval.Web.Localization;

namespace PurchaseApproval.Web.Pages;

[AllowAnonymous]
public sealed class LanguageModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public IActionResult OnPost(string? code, string? returnUrl)
    {
        if (LanguageCookieRequestCultureProvider.IsSupported(code))
        {
            Response.Cookies.Append(
                LanguageCookieRequestCultureProvider.CookieName,
                code!.ToLowerInvariant(),
                new CookieOptions
                {
                    // One year; the preference is not sensitive, so it is not HttpOnly-critical,
                    // but there is no reason for scripts to read it either.
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                    IsEssential = true
                });
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
