using System.Security.Cryptography;
// URL-safe encoding is used for the private tracking token.
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
// This service writes requests and their child records through the application context.
using PurchaseApproval.Data;
// These are the request and attachment entities created during submission.
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
        string submissionKey,
        CancellationToken cancellationToken = default);

    /// <summary>Submits a request on behalf of the authenticated administrator.</summary>
    Task<PaymentRequestSubmissionResult> SubmitAsync(
        ApplicationUserProfile requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        string submissionKey,
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
    string? TrackingToken,
    bool IsDuplicate = false);

/// <summary>Safe profile values copied from a privileged application account into a request snapshot.</summary>
public sealed record ApplicationUserProfile(
    int Id,
    string FullName,
    string Username,
    string? PhoneNumber,
    string? Department,
    string? Branch,
    string? JobTitle);

/// <summary>Normalizes employee and administrator profiles for the shared submission rules.</summary>
public sealed record PaymentRequestRequester(
    RequesterType Type,
    int Id,
    string FullName,
    string Username,
    string? PhoneNumber,
    string? Department,
    string? Branch,
    string? JobTitle)
{
    public static PaymentRequestRequester FromEmployee(EmployeeProfile profile) => new(
        RequesterType.Employee,
        profile.Id,
        profile.FullName,
        profile.Username,
        profile.PhoneNumber,
        profile.Department,
        profile.Branch,
        profile.JobTitle);

    public static PaymentRequestRequester FromApplicationUser(ApplicationUserProfile profile) => new(
        RequesterType.Administrator,
        profile.Id,
        profile.FullName,
        profile.Username,
        profile.PhoneNumber,
        profile.Department,
        profile.Branch,
        profile.JobTitle);
}

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
        string submissionKey,
        CancellationToken cancellationToken = default)
        => await SubmitAsync(
            PaymentRequestRequester.FromEmployee(requester),
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            submissionKey,
            cancellationToken);

    /// <inheritdoc />
    public async Task<PaymentRequestSubmissionResult> SubmitAsync(
        ApplicationUserProfile requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        string submissionKey,
        CancellationToken cancellationToken = default)
        => await SubmitAsync(
            PaymentRequestRequester.FromApplicationUser(requester),
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            submissionKey,
            cancellationToken);

    /// <summary>Persists either requester type through one consistent validation path.</summary>
    private async Task<PaymentRequestSubmissionResult> SubmitAsync(
        PaymentRequestRequester requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        string submissionKey,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rawSubmissionKeyHash = PaymentRequestSubmissionRules.HashSubmissionKey(submissionKey);
        var submissionKeyHash = PaymentRequestSubmissionRules.HashSubmissionKey(
            $"{requester.Type}:{requester.Id}:{rawSubmissionKeyHash}");
        var existing = await dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.SubmissionKeyHash == submissionKeyHash)
            .Where(item => requester.Type == RequesterType.Employee
                ? item.EmployeeRequesterId == requester.Id
                : item.ApplicationUserRequesterId == requester.Id)
            .Select(item => new { item.Id, item.ReferenceNumber })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new PaymentRequestSubmissionResult(
                existing.Id, existing.ReferenceNumber, null, IsDuplicate: true);
        }

        // Validate all business input before allocating a reference or private tracking token.
        var request = PaymentRequestSubmissionRules.CreateRequest(
            requester,
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            string.Empty,
            string.Empty,
            now);

        var reference = PaymentRequestSubmissionRules.CreateReference(now);
        var trackingToken = PaymentRequestSubmissionRules.CreateTrackingToken();
        request.ReferenceNumber = reference;
        request.TrackingTokenHash = PaymentRequestSubmissionRules.HashTrackingToken(trackingToken);
        request.SubmissionKeyHash = submissionKeyHash;

        dbContext.PaymentRequests.Add(request);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            dbContext.Entry(request).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            existing = await dbContext.PaymentRequests
                .AsNoTracking()
                .Where(item => item.SubmissionKeyHash == submissionKeyHash)
                .Where(item => requester.Type == RequesterType.Employee
                    ? item.EmployeeRequesterId == requester.Id
                    : item.ApplicationUserRequesterId == requester.Id)
                .Select(item => new { item.Id, item.ReferenceNumber })
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new PaymentRequestSubmissionResult(
                existing.Id, existing.ReferenceNumber, null, IsDuplicate: true);
        }

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
        => CreateRequest(
            PaymentRequestRequester.FromEmployee(requester),
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            referenceNumber,
            trackingTokenHash,
            submittedAtUtc);

    /// <summary>Creates an administrator-owned request while preserving the administrator profile snapshot.</summary>
    public static PaymentRequest CreateRequest(
        ApplicationUserProfile requester,
        IReadOnlyList<PaymentRequestLineInput> lines,
        IReadOnlyList<PaymentRequestAttachmentInput> attachments,
        string payeeName,
        string justification,
        string? requestedAccountOrCardNumber,
        string referenceNumber,
        string trackingTokenHash,
        DateTimeOffset submittedAtUtc)
        => CreateRequest(
            PaymentRequestRequester.FromApplicationUser(requester),
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            referenceNumber,
            trackingTokenHash,
            submittedAtUtc);

    /// <summary>Creates a validated request entity for an employee or administrator requester.</summary>
    public static PaymentRequest CreateRequest(
        PaymentRequestRequester requester,
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
        if (requester is null || requester.Id <= 0
            || !Enum.IsDefined(requester.Type))
        {
            errors.Add("A valid payment requester is required.");
        }

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

        // Validation above guarantees the requester profile is present and has a supported type.
        var validRequester = requester!;

        return new PaymentRequest
        {
            ReferenceNumber = referenceNumber,
            RequesterType = validRequester.Type,
            EmployeeRequesterId = validRequester.Type == RequesterType.Employee ? validRequester.Id : null,
            ApplicationUserRequesterId = validRequester.Type == RequesterType.Administrator ? validRequester.Id : null,
            RequesterFullNameSnapshot = validRequester.FullName,
            RequesterUsernameSnapshot = validRequester.Username,
            RequesterPhoneNumberSnapshot = validRequester.PhoneNumber,
            RequesterDepartmentSnapshot = validRequester.Department,
            RequesterBranchSnapshot = validRequester.Branch,
            RequesterJobTitleSnapshot = validRequester.JobTitle,
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

    public static string CreateSubmissionKey()
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashSubmissionKey(string submissionKey)
    {
        if (string.IsNullOrWhiteSpace(submissionKey)
            || submissionKey.Length is < 32 or > 128)
        {
            throw new PaymentRequestValidationException(
                ["This submission form is invalid or expired. Reload it and try again."]);
        }

        return Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(submissionKey)));
    }

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
