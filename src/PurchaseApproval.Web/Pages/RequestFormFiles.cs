// Shared upload reader for the employee and administrator request forms. Copies each posted
// file into memory only up to the configured limit and reports oversize files as field errors,
// so the two pages cannot drift apart in how they treat attachments. Content-type detection
// happens later in PaymentRequestSubmissionRules; this step only bounds size.
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Pages;

public static class RequestFormFiles
{
    public static async Task<List<PaymentRequestAttachmentInput>> ReadAsync(
        IReadOnlyList<IFormFile> files,
        long maximumBytes,
        ModelStateDictionary modelState,
        IStringLocalizer localizer,
        CancellationToken cancellationToken)
    {
        var uploaded = new List<PaymentRequestAttachmentInput>(files.Count);
        foreach (var file in files)
        {
            if (file.Length < 1 || file.Length > maximumBytes)
            {
                modelState.AddModelError("Attachments",
                    localizer["{0}: each file must be between 1 byte and 5 MiB.", Path.GetFileName(file.FileName)]);
                continue;
            }

            await using var stream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(stream, cancellationToken);
            if (stream.Length > maximumBytes)
            {
                modelState.AddModelError("Attachments",
                    localizer["{0}: file is too large.", Path.GetFileName(file.FileName)]);
                continue;
            }

            uploaded.Add(new PaymentRequestAttachmentInput(file.FileName, file.ContentType, stream.ToArray()));
        }

        return uploaded;
    }
}
