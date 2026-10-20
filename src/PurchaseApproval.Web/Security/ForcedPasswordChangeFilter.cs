// Confines a staff account that still holds a temporary password to the change-password page.
//
// Program.cs stores the MustChangePassword flag in HttpContext.Items during cookie validation
// (the user row is already loaded there, so this costs no extra query). This page filter then
// redirects every other page request until a personal password has been chosen. Sign-out and the
// language switch stay reachable so the person is never trapped.
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PurchaseApproval.Security;

public sealed class ForcedPasswordChangeFilter : IPageFilter
{
    public const string ItemsKey = "PurchaseApproval.StaffMustChangePassword";

    private static readonly string[] AllowedPaths =
    [
        "/account/change-password",
        "/account/logout",
        "/language"
    ];

    public void OnPageHandlerSelected(PageHandlerSelectedContext context)
    {
    }

    public void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Items[ItemsKey] is not true)
        {
            return;
        }

        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (AllowedPaths.Any(allowed => path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        context.Result = new RedirectResult("/account/change-password?required=true");
    }

    public void OnPageHandlerExecuted(PageHandlerExecutedContext context)
    {
    }
}
