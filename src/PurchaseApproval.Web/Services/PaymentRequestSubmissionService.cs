using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using PurchaseApproval.Data;
using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public interface IPaymentRequestSubmissionService
{
    Task<PaymentRequestSubmissionResult> SubmitAsync(
        EmployeeProfile requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        CancellationToken cancellationToken = default);
}

public sealed record PaymentRequestLineInput(
    string Description,
    long AmountRials,
    bool HasInvoice,
    string? Remarks);

public sealed record PaymentRequestAttachmentInput(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record PaymentRequestSubmissionResult(
    int RequestId,
    string ReferenceNumber,
    string TrackingToken);

public sealed class PaymentRequestValidationException(IReadOnlyList<string> errors)
    : Exception("The payment request is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class PaymentRequestSubmissionService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IPaymentRequestSubmissionService
{
    public async Task<PaymentRequestSubmissionResult> SubmitAsync(
        EmployeeProfile requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var reference = PaymentRequestSubmissionRules.CreateReference(now);
        var trackingToken = PaymentRequestSubmissionRules.CreateTrackingToken();
        var request = PaymentRequestSubmissionRules.CreateRequest(
            requester,
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            reference,
            PaymentRequestSubmissionRules.HashTrackingToken(trackingToken),
            now);

        dbContext.PaymentRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentRequestSubmissionResult(request.Id, reference, trackingToken);
    }
}

public static class PaymentRequestSubmissionRules
{
    public const int MaximumLines = 50;
    public const int MaximumAttachments = 5;
    public const int MaximumAttachmentBytes = 5 * 1024 * 1024;
    public const long MaximumTotalRials = 10_000_000_000;

    public static PaymentRequest CreateRequest(
        EmployeeProfile requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        string referenceNumber,
        string trackingTokenHash,
        DateTimeOffset submittedAtUtc)
    {
        var errors = new List<string>();
        var cleanPayee = RequiredText(payeeName, 200, "Payee name", errors);
        var cleanJustification = RequiredText(justification, 2000, "Payment reason", errors);
        var cleanAccount = OptionalText(requestedAccountOrCardNumber, 100,
            "Account or card number", errors);

        if (lines.Count is < 1 or > MaximumLines)
        {
            errors.Add($"Add between 1 and {MaximumLines} expense rows.");
        }

        long total = 0;
        var entities = new List<PaymentRequestLine>(Math.Min(lines.Count, MaximumLines));
        foreach (var (line, index) in lines.Take(MaximumLines).Select((value, index) => (value, index)))
        {
            var description = RequiredText(line.Description, 500, $"Expense row {index + 1} description", errors);
            var remarks = OptionalText(line.Remarks, 1000, $"Expense row {index + 1} remarks", errors);
            if (line.AmountRials <= 0)
            {
                errors.Add($"Expense row {index + 1} amount must be greater than zero.");
            }

            try
            {
                total = checked(total + line.AmountRials);
            }
            catch (OverflowException)
            {
                errors.Add("The request total is too large.");
                total = long.MaxValue;
            }

            entities.Add(new PaymentRequestLine
            {
                Description = description,
                AmountRials = line.AmountRials,
                HasInvoice = line.HasInvoice,
                Remarks = remarks,
                DisplayOrder = index
            });
        }

        if (total > MaximumTotalRials)
        {
            errors.Add($"The total cannot exceed {MaximumTotalRials:N0} rials.");
        }

        var attachmentEntities = ValidateAttachments(attachments, errors);
        if (errors.Count != 0)
        {
            throw new PaymentRequestValidationException(errors);
        }

        return new PaymentRequest
        {
            ReferenceNumber = referenceNumber,
            RequesterType = RequesterType.Employee,
            EmployeeRequesterId = requester.Id,
            RequesterFullNameSnapshot = requester.FullName,
            RequesterUsernameSnapshot = requester.Username,
            RequesterPhoneNumberSnapshot = requester.PhoneNumber,
            RequesterDepartmentSnapshot = requester.Department,
            RequesterBranchSnapshot = requester.Branch,
            RequesterJobTitleSnapshot = requester.JobTitle,
            PayeeName = cleanPayee,
            Justification = cleanJustification,
            RequestedAccountOrCardNumber = cleanAccount,
            TotalAmountRials = total,
            Status = PaymentRequestStatus.Pending,
            SubmittedAtUtc = submittedAtUtc,
            TrackingTokenHash = trackingTokenHash,
            Lines = entities,
            Attachments = attachmentEntities
        };
    }

    public static string CreateReference(DateTimeOffset now)
        => $"PR-{now:yyyy}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";

    public static string CreateTrackingToken()
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashTrackingToken(string token)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    public static bool VerifyTrackingToken(string token, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != 32) return false;
        var actual = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static List<PaymentRequestAttachment> ValidateAttachments(
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        ICollection<string> errors)
    {
        var entities = new List<PaymentRequestAttachment>();
        if (attachments.Count > MaximumAttachments)
        {
            errors.Add($"Attach no more than {MaximumAttachments} files.");
        }

        foreach (var (file, index) in attachments.Take(MaximumAttachments)
                     .Select((value, index) => (value, index)))
        {
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
            {
                errors.Add($"Attachment {index + 1} has an invalid file name.");
            }

            if (file.Content.Length is < 1 or > MaximumAttachmentBytes)
            {
                errors.Add($"Attachment {index + 1} must be between 1 byte and 5 MiB.");
                continue;
            }

            var canonicalType = DetectContentType(file.Content);
            if (canonicalType is null)
            {
                errors.Add($"Attachment {index + 1} must be a PDF, JPEG, or PNG file.");
                continue;
            }

            entities.Add(new PaymentRequestAttachment
            {
                OriginalFileName = name,
                ContentType = canonicalType,
                FileSizeBytes = file.Content.LongLength,
                Sha256Hash = Convert.ToHexString(SHA256.HashData(file.Content)),
                Content = file.Content
            });
        }

        return entities;
    }

    private static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> pdf = "%PDF-"u8;
        ReadOnlySpan<byte> jpeg = [0xFF, 0xD8, 0xFF];
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.StartsWith(pdf)) return "application/pdf";
        if (content.StartsWith(jpeg)) return "image/jpeg";
        if (content.StartsWith(png)) return "image/png";
        return null;
    }

    private static string RequiredText(string? value, int maximumLength, string label,
        ICollection<string> errors)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) errors.Add($"{label} is required.");
        if (clean.Length > maximumLength) errors.Add($"{label} is too long.");
        return clean;
    }

    private static string? OptionalText(string? value, int maximumLength, string label,
        ICollection<string> errors)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean?.Length > maximumLength) errors.Add($"{label} is too long.");
        return clean;
    }
}
