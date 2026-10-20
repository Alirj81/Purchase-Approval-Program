// Creates Pending payment requests for employees and administrators through one validation path.
//
// Order of operations matters for the business rules:
//   1. idempotent replay check (a resubmitted form returns the original request, consuming nothing);
//   2. full validation, including the 10,000,000,000 IRR ceiling (invalid input consumes no quota);
//   3. inside one database transaction: reserve one unit of the UTC-day quota, then insert the request.
// Reference numbers and tracking tokens are generated only after validation succeeds.
using System.Security.Cryptography;
// URL-safe encoding is used for the private tracking token.
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PurchaseApproval.Configuration;
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

/// <summary>Configured attachment allowlist; defaults match the historical hard-coded values.</summary>
public sealed record AttachmentPolicy(
    int MaximumCount,
    long MaximumBytes,
    IReadOnlySet<string> AllowedContentTypes)
{
    public static readonly AttachmentPolicy Default = new(
        PaymentRequestSubmissionRules.MaximumAttachments,
        PaymentRequestSubmissionRules.MaximumAttachmentBytes,
        new HashSet<string>(["application/pdf", "image/jpeg", "image/png"], StringComparer.Ordinal));

    public static AttachmentPolicy FromOptions(ApplicationLimitsOptions options) => new(
        options.MaximumAttachmentCount,
        options.MaximumAttachmentBytes,
        new HashSet<string>(options.EffectiveAllowedAttachmentContentTypes, StringComparer.Ordinal));
}

public sealed class PaymentRequestValidationException(IReadOnlyList<LocalizableMessage> messages)
    : ValidationMessagesException("The payment request is invalid.", messages);

/// <summary>Raised when the requester already created the daily maximum of requests.</summary>
public sealed class SubmissionQuotaExceededException(int dailyLimit)
    : ValidationMessagesException(
        "The daily submission quota is exhausted.",
        [new LocalizableMessage(
            "You have already created {0} requests today (UTC day). Try again after 00:00 UTC or contact the company.",
            dailyLimit)])
{
    public int DailyLimit { get; } = dailyLimit;
}

public sealed class PaymentRequestSubmissionService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    ISubmissionQuotaService quota,
    IOptions<ApplicationLimitsOptions> limits) : IPaymentRequestSubmissionService
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
        var existing = await FindExistingAsync(requester, submissionKeyHash, cancellationToken);
        if (existing is not null)
        {
            return existing;
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
            now,
            AttachmentPolicy.FromOptions(limits.Value));

        var reference = PaymentRequestSubmissionRules.CreateReference(now);
        var trackingToken = PaymentRequestSubmissionRules.CreateTrackingToken();
        request.ReferenceNumber = reference;
        request.TrackingTokenHash = PaymentRequestSubmissionRules.HashTrackingToken(trackingToken);
        request.SubmissionKeyHash = submissionKeyHash;

        // Quota and insert share one transaction so a full quota rolls the insert back and a
        // failed insert releases the reserved unit. InMemory (tests) has no transactions.
        var useTransaction = dbContext.Database.IsRelational();
        await using var transaction = useTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var reserved = await quota.TryReserveAsync(
                requester.Type, requester.Id, now, limits.Value.DailySubmissionQuota, cancellationToken);
            if (!reserved)
            {
                throw new SubmissionQuotaExceededException(limits.Value.DailySubmissionQuota);
            }

            dbContext.PaymentRequests.Add(request);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.Entry(request).State = EntityState.Detached;
            var failure = DatabaseErrorClassifier.Classify(exception);
            if (failure.Kind == DatabaseFailureKind.UniqueViolation)
            {
                // The same form was posted twice at once; the first insert won.
                existing = await FindExistingAsync(requester, submissionKeyHash, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
            }

            if (failure.Kind == DatabaseFailureKind.ConstraintViolation)
            {
                throw new PaymentRequestValidationException(
                    [DatabaseErrorClassifier.DescribeConstraintViolation()]);
            }

            throw;
        }
        catch (SubmissionQuotaExceededException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            dbContext.Entry(request).State = EntityState.Detached;
            throw;
        }

        return new PaymentRequestSubmissionResult(request.Id, reference, trackingToken);
    }

    private async Task<PaymentRequestSubmissionResult?> FindExistingAsync(
        PaymentRequestRequester requester,
        string submissionKeyHash,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.PaymentRequests
            .AsNoTracking()
            .Where(item => item.SubmissionKeyHash == submissionKeyHash)
            .Where(item => requester.Type == RequesterType.Employee
                ? item.EmployeeRequesterId == requester.Id
                : item.ApplicationUserRequesterId == requester.Id)
            .Select(item => new { item.Id, item.ReferenceNumber })
            .SingleOrDefaultAsync(cancellationToken);

        return existing is null
            ? null
            : new PaymentRequestSubmissionResult(existing.Id, existing.ReferenceNumber, null, IsDuplicate: true);
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
        DateTimeOffset submittedAtUtc,
        AttachmentPolicy? attachmentPolicy = null)
        => CreateRequest(
            PaymentRequestRequester.FromEmployee(requester),
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            referenceNumber,
            trackingTokenHash,
            submittedAtUtc,
            attachmentPolicy);

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
        DateTimeOffset submittedAtUtc,
        AttachmentPolicy? attachmentPolicy = null)
        => CreateRequest(
            PaymentRequestRequester.FromApplicationUser(requester),
            lines,
            attachments,
            payeeName,
            justification,
            requestedAccountOrCardNumber,
            referenceNumber,
            trackingTokenHash,
            submittedAtUtc,
            attachmentPolicy);

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
        DateTimeOffset submittedAtUtc,
        AttachmentPolicy? attachmentPolicy = null)
    {
        attachmentPolicy ??= AttachmentPolicy.Default;
        var errors = new List<LocalizableMessage>();
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
            errors.Add(new LocalizableMessage("Add between 1 and {0} expense rows.", MaximumLines));
        }

        long total = 0;
        var entities = new List<PaymentRequestLine>(Math.Min(lines.Count, MaximumLines));
        foreach (var (line, index) in lines.Take(MaximumLines).Select((value, index) => (value, index)))
        {
            var description = RequiredText(line.Description, 500,
                new LocalizableMessage("Expense row {0} description", index + 1), errors);
            var remarks = OptionalText(line.Remarks, 1000,
                new LocalizableMessage("Expense row {0} remarks", index + 1), errors);
            if (line.AmountRials <= 0)
            {
                errors.Add(new LocalizableMessage("Expense row {0} amount must be greater than zero.", index + 1));
            }

            // checked() turns a 64-bit overflow into an exception instead of a silent wrap-around.
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
            errors.Add(new LocalizableMessage(
                "The total cannot exceed {0} rials. Please contact the company to arrange this request in person.",
                MaximumTotalRials.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
        }

        var attachmentEntities = ValidateAttachments(attachments, attachmentPolicy, errors);
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
        AttachmentPolicy policy,
        ICollection<LocalizableMessage> errors)
    {
        var entities = new List<PaymentRequestAttachment>();
        if (attachments.Count > policy.MaximumCount)
        {
            errors.Add(new LocalizableMessage("Attach no more than {0} files.", policy.MaximumCount));
        }

        foreach (var (file, index) in attachments.Take(policy.MaximumCount)
                     .Select((value, index) => (value, index)))
        {
            // Path.GetFileName strips any directory part a browser or attacker might include.
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
            {
                errors.Add(new LocalizableMessage("Attachment {0} has an invalid file name.", index + 1));
            }

            if (file.Content.Length < 1 || file.Content.LongLength > policy.MaximumBytes)
            {
                errors.Add(new LocalizableMessage("Attachment {0} must be between 1 byte and 5 MiB.", index + 1));
                continue;
            }

            // Content sniffing decides the type; the browser-supplied extension and MIME are ignored.
            var canonicalType = DetectContentType(file.Content);
            if (canonicalType is null || !policy.AllowedContentTypes.Contains(canonicalType))
            {
                errors.Add(new LocalizableMessage("Attachment {0} must be a PDF, JPEG, or PNG file.", index + 1));
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

    private static string RequiredText(string? value, int maximumLength, LocalizableMessage label,
        ICollection<LocalizableMessage> errors)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) errors.Add(new LocalizableMessage("{0} is required.", label.ToString()));
        if (clean.Length > maximumLength) errors.Add(new LocalizableMessage("{0} is too long.", label.ToString()));
        return clean;
    }

    private static string? OptionalText(string? value, int maximumLength, LocalizableMessage label,
        ICollection<LocalizableMessage> errors)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean?.Length > maximumLength) errors.Add(new LocalizableMessage("{0} is too long.", label.ToString()));
        return clean;
    }
}
