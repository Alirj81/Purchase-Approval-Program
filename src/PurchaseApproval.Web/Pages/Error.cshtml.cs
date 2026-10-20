// Production error page. It shows a generic message and a correlation ID only; the exception
// itself is written to the server log by the exception-handler middleware under the same
// request ID, so an operator can match a user report to the log line without exposing details.
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PurchaseApproval.Web.Pages;

[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public int? HttpStatus { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public void OnGet(int? status)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        HttpStatus = status;
        if (status is { } code && code is >= 400 and < 600)
        {
            Response.StatusCode = code;
        }
    }
}
