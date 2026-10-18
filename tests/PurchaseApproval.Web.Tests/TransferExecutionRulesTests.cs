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
        Assert.True(transfer.IsClosed);
        Assert.Equal(PaymentRequestStatus.Approved, transfer.PaymentRequest.Status);
        Assert.Equal("1234-5678", transfer.DestinationAccountOrCardNumber);
        Assert.Equal("Amir Accountant", transfer.PerformedByFullNameSnapshot);
        Assert.Equal(RecordedAt, transfer.RecordedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero),
            transfer.TransactionOccurredAtUtc);
        Assert.Equal((short)210, transfer.TransactionUtcOffsetMinutes);
        Assert.Equal(TransferAuditAction.MarkedTransferred, audit.Action);
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
    }

    [Fact]
    public void Administrator_CannotRecordUnsuccessfulOutcome()
    {
        var exception = Assert.Throws<TransferExecutionValidationException>(() =>
            TransferExecutionRules.ApplyOutcome(
                AwaitingTransfer(), 4, "Mina Manager", AppRoles.Administrator,
                ValidInput(TransferStatus.Unsuccessful, TransferMethod.Cash), RecordedAt));

        Assert.Contains(exception.Errors,
            error => error.Contains("not allowed", StringComparison.OrdinalIgnoreCase));
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

    private static TransferOutcomeInput ValidInput(
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
            new DateTime(2026, 10, 18, 12, 30, 0),
            210,
            null);
}
