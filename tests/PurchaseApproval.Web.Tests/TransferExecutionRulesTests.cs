// Verifies the execution-outcome rules: who may record which outcome, method-specific details,
// reported-time resolution and bounds, automatic/early closure and the mandatory closure reason.
using PurchaseApproval.Models;
using PurchaseApproval.Security;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Tests;

public sealed class TransferExecutionRulesTests
{
    private static readonly DateTimeOffset RecordedAt =
        new(2026, 10, 18, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateAwaitingTransfer_KeepsApprovalSeparateAndAuditsCreation()
    {
        var request = ApprovedRequest();

        var transfer = TransferExecutionRules.CreateAwaitingTransfer(
            request, 4, "Mina Manager", AppRoles.Administrator, RecordedAt);

        Assert.Equal(PaymentRequestStatus.Approved, request.Status);
        Assert.Equal(TransferStatus.AwaitingTransfer, transfer.Status);
        Assert.False(transfer.IsClosed);
        var audit = Assert.Single(transfer.AuditEntries);
        Assert.Equal(TransferAuditAction.AwaitingTransferCreated, audit.Action);
        Assert.Equal(RecordedAt, audit.RecordedAtUtc);
    }

    [Fact]
    public void Accountant_CanRecordTransferredWithOccurrenceAndServerTimes()
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(TransferStatus.Transferred, TransferMethod.BankTransfer) with
        {
            DestinationAccountOrCardNumber = "  1234-5678  ",
            BankName = " Test Bank "
        };

        var audit = TransferExecutionRules.ApplyOutcome(
            transfer, 8, "  Amir Accountant  ", AppRoles.Accountant, input, RecordedAt);

        Assert.Equal(TransferStatus.Transferred, transfer.Status);
        Assert.False(transfer.IsClosed);
        Assert.Equal(PaymentRequestStatus.Approved, transfer.PaymentRequest.Status);
        Assert.Equal("1234-5678", transfer.DestinationAccountOrCardNumber);
        Assert.Equal("Amir Accountant", transfer.PerformedByFullNameSnapshot);
        Assert.Equal(RecordedAt, transfer.RecordedAtUtc);
        // 12:30 at +03:30 is 09:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero),
            transfer.TransactionOccurredAtUtc);
        Assert.Equal((short)210, transfer.TransactionUtcOffsetMinutes);
        Assert.Equal(new DateTime(2026, 10, 18, 12, 30, 0), transfer.TransactionEnteredLocalDateTime);
        Assert.Null(transfer.TransactionTimeZoneId);
        Assert.Equal(TransferAuditAction.MarkedTransferred, audit.Action);
        Assert.Equal(RecordedAt.AddHours(24), TransferExecutionRules.GetAutomaticClosureDueAt(transfer));
    }

    [Fact]
    public void ZoneBasedInput_StoresZoneAndResolvedOffsetForThatDate()
    {
        var transfer = AwaitingTransfer();
        // Berlin is +02:00 in July (summer time) even if the device is on +01:00 today.
        var input = ValidInput(TransferStatus.Transferred, TransferMethod.Cash) with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 7, 1, 10, 0, 0), "Europe/Berlin", null)
        };

        TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant, input, new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("Europe/Berlin", transfer.TransactionTimeZoneId);
        Assert.Equal((short)120, transfer.TransactionUtcOffsetMinutes);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), transfer.TransactionOccurredAtUtc);
    }

    [Fact]
    public void Accountant_UnsuccessfulStaysOpenAndFlaggedWithoutChangingApproval()
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(TransferStatus.Unsuccessful, TransferMethod.Cash) with
        {
            FailureReason = "  Cash was unavailable  "
        };

        TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant, input, RecordedAt);

        Assert.Equal(TransferStatus.Unsuccessful, transfer.Status);
        Assert.False(transfer.IsClosed);
        Assert.Equal("Cash was unavailable", transfer.FailureReason);
        Assert.Equal(PaymentRequestStatus.Approved, transfer.PaymentRequest.Status);
        Assert.Equal(RecordedAt.AddHours(48), TransferExecutionRules.GetAutomaticClosureDueAt(transfer));
    }

    [Theory]
    [InlineData(AppRoles.Accountant)]
    [InlineData(AppRoles.Administrator)]
    public void UnsuccessfulOutcome_RequiresAReasonFromEitherRole(string role)
    {
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(
                AwaitingTransfer(),
                8,
                "Executor",
                role,
                ValidInput(TransferStatus.Unsuccessful, TransferMethod.Cash),
                RecordedAt));

        Assert.Contains(exception.Errors,
            error => error.Contains("reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Administrator_CanRecordUnsuccessfulOutcomeWithReason()
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(TransferStatus.Unsuccessful, TransferMethod.Cash) with
        {
            FailureReason = "Bank rejected the transfer"
        };

        TransferExecutionRules.ApplyOutcome(
            transfer, 4, "Mina Manager", AppRoles.Administrator, input, RecordedAt);

        Assert.Equal(TransferStatus.Unsuccessful, transfer.Status);
        Assert.Equal(AppRoles.Administrator, transfer.PerformedByRoleSnapshot);
        Assert.Equal("Bank rejected the transfer", transfer.FailureReason);
    }

    [Fact]
    public void Administrator_CanRecordSuccessfulTransferPersonallyPerformed()
    {
        var transfer = AwaitingTransfer();

        TransferExecutionRules.ApplyOutcome(
            transfer, 4, "Mina Manager", AppRoles.Administrator,
            ValidInput(TransferStatus.Transferred, TransferMethod.Cash), RecordedAt);

        Assert.Equal(TransferStatus.Transferred, transfer.Status);
        Assert.Equal(AppRoles.Administrator, transfer.PerformedByRoleSnapshot);
    }

    [Fact]
    public void Employee_CannotRecordAnOutcome()
    {
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(
                AwaitingTransfer(), 9, "Someone", "Employee",
                ValidInput(TransferStatus.Transferred, TransferMethod.Cash), RecordedAt));

        Assert.Contains(exception.Errors, error => error.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OptionalExternalAccountingReference_IsTrimmedAndAudited()
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(TransferStatus.Transferred, TransferMethod.Cash) with
        {
            ExternalReference = "  EXT-0007  "
        };

        var audit = TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant, input, RecordedAt);

        Assert.Equal("EXT-0007", transfer.ExternalReference);
        Assert.Equal("EXT-0007", audit.ExternalReference);
    }

    [Fact]
    public void ReportedTime_WithinToleranceIsAccepted_BeyondToleranceIsRejected()
    {
        var policy = new TransferTimePolicy(null, TimeSpan.FromMinutes(15));
        var recordedAt = new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

        // 09:14 UTC entered as 12:44 at +03:30: inside tolerance.
        var inside = ValidInput(TransferStatus.Transferred, TransferMethod.Cash) with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 44, 0), null, 210)
        };
        TransferExecutionRules.ApplyOutcome(AwaitingTransfer(), 8, "A", AppRoles.Accountant, inside, recordedAt, policy);

        // 09:16 UTC: one minute beyond tolerance.
        var beyond = inside with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 46, 0), null, 210)
        };
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(AwaitingTransfer(), 8, "A", AppRoles.Accountant, beyond, recordedAt, policy));
        Assert.Contains(exception.Errors, error => error.Contains("later than the server time", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportedTime_BeforeApprovalIsRejected()
    {
        var approvedAt = new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        var policy = new TransferTimePolicy(approvedAt, TimeSpan.FromMinutes(15));
        var input = ValidInput(TransferStatus.Transferred, TransferMethod.Cash) with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 10, 18, 7, 59, 0), "UTC", null)
        };

        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(AwaitingTransfer(), 8, "A", AppRoles.Accountant, input, RecordedAt, policy));

        Assert.Contains(exception.Errors, error => error.Contains("earlier than the approval", StringComparison.Ordinal));

        // Same minute as the approval (which carries seconds) is accepted: entered times have minute precision.
        var sameMinutePolicy = new TransferTimePolicy(approvedAt.AddSeconds(45), TimeSpan.FromMinutes(15));
        var sameMinute = input with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 10, 18, 8, 0, 0), "UTC", null)
        };
        TransferExecutionRules.ApplyOutcome(AwaitingTransfer(), 8, "A", AppRoles.Accountant, sameMinute, RecordedAt, sameMinutePolicy);
    }

    [Fact]
    public void NonexistentAndAmbiguousLocalTimes_AreRejectedWithExplanations()
    {
        // Europe/Berlin 2026-03-29 02:30 never happened; 2026-10-25 02:30 happened twice.
        var nonexistent = ValidInput(TransferStatus.Transferred, TransferMethod.Cash) with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 3, 29, 2, 30, 0), "Europe/Berlin", null)
        };
        var ambiguous = nonexistent with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 10, 25, 2, 30, 0), "Europe/Berlin", null)
        };

        var first = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(AwaitingTransfer(), 8, "A", AppRoles.Accountant, nonexistent, RecordedAt));
        var second = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(AwaitingTransfer(), 8, "A", AppRoles.Accountant, ambiguous, RecordedAt));

        Assert.Contains(first.Errors, error => error.Contains("did not exist", StringComparison.Ordinal));
        Assert.Contains(second.Errors, error => error.Contains("occurred twice", StringComparison.Ordinal));

        // Choosing one of the two valid offsets resolves the ambiguity.
        var chosen = ambiguous with
        {
            Time = new TransactionTimeInput(new DateTime(2026, 10, 25, 2, 30, 0), "Europe/Berlin", 60)
        };
        var transfer = AwaitingTransfer();
        TransferExecutionRules.ApplyOutcome(transfer, 8, "A", AppRoles.Accountant, chosen, new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.Zero));
        Assert.Equal((short)60, transfer.TransactionUtcOffsetMinutes);
    }

    [Theory]
    [InlineData(TransferStatus.Transferred, 24)]
    [InlineData(TransferStatus.Unsuccessful, 48)]
    public void AutomaticClosure_UsesServerRecordedOutcomeTimerAndPreservesOutcome(
        TransferStatus status,
        int hours)
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(status, TransferMethod.Cash) with
        {
            FailureReason = status == TransferStatus.Unsuccessful ? "Attempt failed" : null
        };
        TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant, input, RecordedAt);

        Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyAutomaticClosure(
                transfer,
                RecordedAt.AddHours(hours).AddTicks(-1)));

        var audit = TransferExecutionRules.ApplyAutomaticClosure(
            transfer,
            RecordedAt.AddHours(hours));

        Assert.True(transfer.IsClosed);
        Assert.Equal(status, transfer.Status);
        Assert.Equal(TransferClosureType.Automatic, transfer.ClosureType);
        Assert.Null(transfer.ClosedByUserId);
        Assert.False(string.IsNullOrWhiteSpace(transfer.ClosureReason));
        Assert.Equal(transfer.ClosureReason, audit.ClosureReason);
        Assert.Equal(status == TransferStatus.Transferred
                ? TransferAuditAction.TransferredClosedAutomatically
                : TransferAuditAction.UnsuccessfulClosedAutomatically,
            audit.Action);
    }

    [Fact]
    public void AdministratorCanCloseEarly_WithAReason_WithoutChangingTheRecordedOutcome()
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(TransferStatus.Unsuccessful, TransferMethod.Cash) with
        {
            FailureReason = "Attempt failed",
            ExternalReference = "ACC-9"
        };
        TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant, input, RecordedAt);

        var audit = TransferExecutionRules.ApplyAdministratorClosure(
            transfer,
            4,
            " Mina Manager ",
            AppRoles.Administrator,
            RecordedAt.AddMinutes(5),
            "  Payee confirmed by phone that no retry is needed  ");

        Assert.True(transfer.IsClosed);
        Assert.Equal(TransferStatus.Unsuccessful, transfer.Status);
        Assert.Equal("Attempt failed", transfer.FailureReason);
        Assert.Equal("ACC-9", transfer.ExternalReference);
        Assert.Equal(TransferClosureType.Administrator, transfer.ClosureType);
        Assert.Equal("Mina Manager", transfer.ClosedByFullNameSnapshot);
        Assert.Equal("Payee confirmed by phone that no retry is needed", transfer.ClosureReason);
        Assert.Equal(transfer.ClosureReason, audit.ClosureReason);
        Assert.Equal(TransferAuditAction.ClosedByAdministrator, audit.Action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EarlyClosure_RequiresAReason(string? reason)
    {
        var transfer = AwaitingTransfer();
        TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant,
            ValidInput(TransferStatus.Transferred, TransferMethod.Cash), RecordedAt);

        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyAdministratorClosure(
                transfer, 4, "Mina Manager", AppRoles.Administrator, RecordedAt.AddMinutes(1), reason));

        Assert.Contains(exception.Errors, error => error.Contains("reason", StringComparison.OrdinalIgnoreCase));
        Assert.False(transfer.IsClosed);
    }

    [Fact]
    public void EarlyClosure_IsRefusedForAnItemStillAwaitingItsFirstOutcome()
    {
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyAdministratorClosure(
                AwaitingTransfer(), 4, "Mina Manager", AppRoles.Administrator, RecordedAt, "Reason"));

        Assert.Contains(exception.Errors, error => error.Contains("before closing", StringComparison.Ordinal));
    }

    [Fact]
    public void AccountantCannotCloseAnExecutionItemEarly()
    {
        var transfer = AwaitingTransfer();
        TransferExecutionRules.ApplyOutcome(
            transfer,
            8,
            "Amir Accountant",
            AppRoles.Accountant,
            ValidInput(TransferStatus.Transferred, TransferMethod.Cash),
            RecordedAt);

        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyAdministratorClosure(
                transfer,
                8,
                "Amir Accountant",
                AppRoles.Accountant,
                RecordedAt.AddMinutes(1),
                "Reason"));

        Assert.Contains(exception.Errors,
            error => error.Contains("administrator", StringComparison.OrdinalIgnoreCase));
        Assert.False(transfer.IsClosed);
    }

    [Fact]
    public void BankTransfer_RequiresActualDestination()
    {
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(
                AwaitingTransfer(), 8, "Amir Accountant", AppRoles.Accountant,
                ValidInput(TransferStatus.Transferred, TransferMethod.BankTransfer), RecordedAt));

        Assert.Contains(exception.Errors,
            error => error.Contains("actual destination", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cheque_RequiresAllChequeFields()
    {
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(
                AwaitingTransfer(), 8, "Amir Accountant", AppRoles.Accountant,
                ValidInput(TransferStatus.Transferred, TransferMethod.Cheque), RecordedAt));

        Assert.Contains(exception.Errors,
            error => error.Contains("Cheque number", StringComparison.Ordinal));
    }

    [Fact]
    public void RecordedOutcome_CannotBeRecordedAgain()
    {
        var transfer = AwaitingTransfer();
        var input = ValidInput(TransferStatus.Transferred, TransferMethod.Cash);
        TransferExecutionRules.ApplyOutcome(
            transfer, 8, "Amir Accountant", AppRoles.Accountant, input, RecordedAt);

        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(
                transfer, 8, "Amir Accountant", AppRoles.Accountant, input, RecordedAt));

        Assert.Contains(exception.Errors,
            error => error.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    private static PaymentRequest ApprovedRequest() => new()
    {
        Id = 21,
        ReferenceNumber = "PR-2026-TRANSFER",
        Status = PaymentRequestStatus.Approved
    };

    private static TransferOutcome AwaitingTransfer()
    {
        var request = ApprovedRequest();
        return new TransferOutcome
        {
            PaymentRequest = request,
            Status = TransferStatus.AwaitingTransfer
        };
    }

    /// <summary>Cash outcome entered as 12:30 local with an explicit +03:30 offset (no device zone).</summary>
    internal static TransferOutcomeInput ValidInput(
        TransferStatus status,
        TransferMethod method) => new(
            status,
            method,
            null,
            null,
            null,
            null,
            null,
            null,
            new TransactionTimeInput(new DateTime(2026, 10, 18, 12, 30, 0), null, 210),
            null,
            null);
}
