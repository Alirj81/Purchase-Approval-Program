//  This code-behind file defines the ErrorModel class for the Error Razor page in the PurchaseApproval.Web project.
//  The ErrorModel class provides properties and methods to handle error information, including the request ID and whether it should be displayed.

using System.Diagnostics;
// Provides response-cache metadata for the error response.
using Microsoft.AspNetCore.Mvc;
// Supplies the PageModel base class.
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PurchaseApproval.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]//  Disables caching for the error page to ensure that the most recent error information is always displayed.
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}

