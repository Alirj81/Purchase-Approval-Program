// Maps stored enum values to the English label keys used in views (L[Labels.Status(x)]).
//
// Enum names are storage identifiers and stay English in the database and exports; the labels
// here are what people see, so a value such as TransferStatus.AwaitingTransfer can read
// "Awaiting payment record" without renaming the enum. Keep this the single place where a value
// gains a human label, so Persian translations need only one key per value.
using PurchaseApproval.Models;
using PurchaseApproval.Services;

namespace PurchaseApproval.Web.Localization;

public static class Labels
{
    public static string Status(PaymentRequestStatus status) => status switch
    {
        PaymentRequestStatus.Pending => "Pending",
        PaymentRequestStatus.Approved => "Approved",
        PaymentRequestStatus.Rejected => "Rejected",
        _ => status.ToString()
    };

    public static string Decision(ApprovalDecisionType decision) => decision switch
    {
        ApprovalDecisionType.Approved => "Approved",
        ApprovalDecisionType.Rejected => "Rejected",
        _ => decision.ToString()
    };

    public static string Transfer(TransferStatus status) => status switch
    {
        TransferStatus.AwaitingTransfer => "Awaiting payment record",
        TransferStatus.Transferred => "Transferred",
        TransferStatus.Unsuccessful => "Unsuccessful",
        _ => status.ToString()
    };

    /// <summary>Requester-facing payment label: no method, destination or internal reason.</summary>
    public static string PaymentForRequester(TransferStatus? status) => status switch
    {
        TransferStatus.AwaitingTransfer => "Awaiting payment record",
        TransferStatus.Transferred => "Payment recorded",
        TransferStatus.Unsuccessful => "Payment attempt unsuccessful",
        _ => "Not applicable"
    };

    public static string Method(TransferMethod method) => method switch
    {
        TransferMethod.Cash => "Cash",
        TransferMethod.BankTransfer => "Bank transfer",
        TransferMethod.Cheque => "Cheque",
        TransferMethod.Other => "Other",
        _ => method.ToString()
    };

    public static string Closure(TransferClosureType closure) => closure switch
    {
        TransferClosureType.Automatic => "Automatic",
        TransferClosureType.Administrator => "Administrator",
        _ => closure.ToString()
    };

    public static string RequesterType(RequesterType type) => type switch
    {
        Models.RequesterType.Employee => "Employee",
        Models.RequesterType.Administrator => "Administrator",
        _ => type.ToString()
    };

    public static string AccessStatus(EmployeeAccessStatus status) => status switch
    {
        EmployeeAccessStatus.Active => "Active",
        EmployeeAccessStatus.Disabled => "Disabled",
        EmployeeAccessStatus.Revoked => "Revoked",
        EmployeeAccessStatus.Deleted => "Deleted",
        _ => status.ToString()
    };

    public static string Role(string role) => role switch
    {
        "Administrator" => "Administrator",
        "Accountant" => "Accountant",
        _ => role
    };

    public static string TransferAudit(TransferAuditAction action) => action switch
    {
        TransferAuditAction.AwaitingTransferCreated => "Entered the payment queue",
        TransferAuditAction.MarkedTransferred => "Recorded as transferred",
        TransferAuditAction.MarkedUnsuccessful => "Recorded as unsuccessful",
        TransferAuditAction.ClosedByAdministrator => "Closed by administrator",
        TransferAuditAction.TransactionDetailsCorrected => "Transaction details corrected",
        TransferAuditAction.TransferredClosedAutomatically => "Closed automatically (transferred)",
        TransferAuditAction.UnsuccessfulClosedAutomatically => "Closed automatically (unsuccessful)",
        _ => action.ToString()
    };

    public static string TimestampField(HistoryTimestampField field) => field switch
    {
        HistoryTimestampField.Submission => "Submission",
        HistoryTimestampField.Decision => "Decision",
        HistoryTimestampField.ExecutionRecording => "Execution recording",
        HistoryTimestampField.Closure => "Closure",
        _ => field.ToString()
    };

    public static string ClosureFilter(HistoryClosureFilter filter) => filter switch
    {
        HistoryClosureFilter.All => "All",
        HistoryClosureFilter.Open => "Open",
        HistoryClosureFilter.Closed => "Closed",
        _ => filter.ToString()
    };

    public static string Sort(HistorySortDirection direction) => direction switch
    {
        HistorySortDirection.NewestFirst => "Newest first",
        HistorySortDirection.OldestFirst => "Oldest first",
        _ => direction.ToString()
    };
}
