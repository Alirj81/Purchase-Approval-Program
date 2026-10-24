using PurchaseApproval.Models;

namespace PurchaseApproval.Services;

public enum ReportLanguage
{
    English = 1,
    Persian = 2
}

/// <summary>Keeps workbook wording separate from stored enum names and database values.</summary>
internal sealed class ExcelReportCopy(ReportLanguage language)
{
    private static readonly IReadOnlyDictionary<string, string> Persian =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Application decision and execution record"] = "گزارش تصمیم و پرداخت درخواست",
            ["Decision record"] = "گزارش درخواست",
            ["This record documents the application decision and any recorded execution outcome. It is not a bank receipt or a cryptographically verified signature."] = "این گزارش، تصمیم نهایی و نتیجهٔ ثبت‌شدهٔ پرداخت را نشان می‌دهد و رسید بانکی یا امضای دیجیتال نیست.",
            ["Request and requester snapshot"] = "مشخصات درخواست",
            ["Request reference"] = "شمارهٔ پیگیری",
            ["Request status"] = "وضعیت درخواست",
            ["Requester type"] = "نوع درخواست‌دهنده",
            ["Submitted (UTC)"] = "زمان ثبت (UTC)",
            ["Requester name"] = "نام درخواست‌دهنده",
            ["Username"] = "نام کاربری",
            ["Phone"] = "شمارهٔ تلفن",
            ["Department"] = "واحد",
            ["Branch"] = "شعبه",
            ["Job title"] = "سمت",
            ["Intended payee"] = "گیرندهٔ وجه",
            ["Requested account/card"] = "حساب یا کارت درخواستی",
            ["Payment purpose"] = "علت پرداخت",
            ["Attachments"] = "پیوست‌ها",
            ["No attachments recorded"] = "پیوستی ثبت نشده است",
            ["Expense rows"] = "ردیف‌های هزینه",
            ["Description"] = "شرح",
            ["Invoice"] = "فاکتور",
            ["Remarks"] = "توضیحات",
            ["Amount (IRR)"] = "مبلغ (ریال)",
            ["Authoritative saved total"] = "جمع نهایی ثبت‌شده",
            ["Amount in words"] = "مبلغ به حروف",
            ["Decision"] = "تصمیم",
            ["Decision result"] = "نتیجهٔ تصمیم",
            ["Decision time (UTC)"] = "زمان تصمیم (UTC)",
            ["Deciding administrator"] = "مدیر تصمیم‌گیرنده",
            ["Administrator user ID"] = "شناسهٔ مدیر",
            ["Self-approved"] = "تأیید درخواست خود",
            ["Rejection explanation"] = "علت رد",
            ["Recorded execution outcome"] = "نتیجهٔ پرداخت",
            ["Transfer status"] = "وضعیت پرداخت",
            ["Execution statement"] = "خلاصهٔ پرداخت",
            ["Method"] = "روش پرداخت",
            ["Executor"] = "مجری پرداخت",
            ["Executor role"] = "نقش مجری",
            ["Executor user ID"] = "شناسهٔ مجری",
            ["Recording time (UTC)"] = "زمان ثبت در سرور (UTC)",
            ["Reported transaction time"] = "زمان اعلام‌شدهٔ تراکنش",
            ["Destination account/card"] = "حساب یا کارت مقصد",
            ["Bank"] = "بانک",
            ["Cheque number"] = "شمارهٔ چک",
            ["Cheque date"] = "تاریخ چک",
            ["Cheque payee"] = "گیرندهٔ چک",
            ["Failure explanation"] = "علت ناموفق بودن",
            ["External accounting reference"] = "شمارهٔ مرجع حسابداری",
            ["Operational state"] = "وضعیت پرونده",
            ["Closure type"] = "نوع بستن پرونده",
            ["Closed at (UTC)"] = "زمان بستن (UTC)",
            ["Closed by"] = "بسته‌شده توسط",
            ["Closure reason"] = "علت بستن پرونده",
            ["Approved request export"] = "گزارش درخواست‌های تأییدشده",
            ["Approved requests"] = "درخواست‌های تأییدشده",
            ["Selected period"] = "بازهٔ گزارش",
            ["Date basis"] = "مبنای تاریخ",
            ["Reporting timezone"] = "منطقهٔ زمانی گزارش",
            ["Displayed calendar"] = "تقویم نمایش",
            ["Generated (report timezone)"] = "زمان تهیهٔ گزارش",
            ["Record count"] = "تعداد درخواست‌ها",
            ["Export total (IRR)"] = "جمع گزارش (ریال)",
            ["Request reference"] = "شمارهٔ پیگیری",
            ["Requester name"] = "نام درخواست‌دهنده",
            ["Total (IRR)"] = "جمع (ریال)",
            ["Currency"] = "واحد پول",
            ["Submitted (report timezone)"] = "زمان ثبت",
            ["Approver user ID"] = "شناسهٔ تأییدکننده",
            ["Approved by"] = "تأییدکننده",
            ["Approved (report timezone)"] = "زمان تأیید",
            ["Transfer status"] = "وضعیت پرداخت",
            ["Reported transaction time and offset"] = "زمان اعلام تراکنش و اختلاف UTC",
            ["Server recorded (report timezone)"] = "زمان ثبت در سرور",
            ["Destination account/card"] = "حساب یا کارت مقصد",
            ["Cheque payee"] = "گیرندهٔ چک",
            ["Failure explanation"] = "علت ناموفق بودن",
            ["Closed (report timezone)"] = "زمان بستن",
            ["Not recorded"] = "ثبت نشده",
            ["Yes"] = "بله",
            ["No"] = "خیر",
            ["Approved"] = "تأیید شده",
            ["Rejected"] = "رد شده",
            ["Pending"] = "در انتظار بررسی",
            ["Employee"] = "کارمند",
            ["Administrator"] = "مدیر",
            ["Accountant"] = "حسابدار",
            ["AwaitingTransfer"] = "در انتظار ثبت پرداخت",
            ["Transferred"] = "پرداخت شده",
            ["Unsuccessful"] = "ناموفق",
            ["Cash"] = "نقدی",
            ["BankTransfer"] = "انتقال بانکی",
            ["Cheque"] = "چک",
            ["Other"] = "سایر",
            ["Open"] = "باز",
            ["Closed"] = "بسته‌شده",
            ["Automatic"] = "خودکار",
            ["Approval decision time"] = "زمان تصمیم مدیریت",
            ["Request submission time"] = "زمان ثبت درخواست",
            ["Gregorian"] = "میلادی",
            ["Persian (Solar Hijri)"] = "هجری شمسی",
            ["Automatic closure service"] = "سرویس بستن خودکار"
        };

    public bool IsPersian => language == ReportLanguage.Persian;

    public string this[string english] =>
        IsPersian && Persian.TryGetValue(english, out var translated) ? translated : english;

    public string MissingValue => this["Not recorded"];

    public string RequesterType(RequesterType value) => this[value.ToString()];

    public string RequestStatus(PaymentRequestStatus value) => this[value.ToString()];

    public string Decision(ApprovalDecisionType value) => this[value.ToString()];

    public string Transfer(TransferStatus value) => this[value.ToString()];

    public string Method(TransferMethod value) => this[value.ToString()];

    public string Closure(TransferClosureType value) => this[value.ToString()];

    public string Role(string? value) => value is null ? MissingValue : this[value];

    public string AttachmentSummary(int count) => count == 0
        ? this["No attachments recorded"]
        : IsPersian
            ? $"{count:N0} پیوست ثبت شده است؛ محتوای فایل‌ها در گزارش قرار ندارد"
            : $"{count:N0} attachment(s) recorded; file content is not embedded";

    public string AmountInWords(long value) => IsPersian
        ? PersianRialAmountInWords.Format(value)
        : RialAmountInWords.Format(value);

    public string SelfApproval(bool value) => IsPersian
        ? value
            ? "بله؛ مدیر تصمیم‌گیرنده همان درخواست‌دهنده است"
            : "خیر"
        : value
            ? "Yes; the deciding administrator is also the request's requester"
            : "No";

    public string RejectionExplanation(DecisionReportSnapshot decision) =>
        decision.DecisionType == ApprovalDecisionType.Rejected
            ? decision.RejectionReason ?? (IsPersian ? "علتی ثبت نشده است" : "None was provided")
            : IsPersian ? "برای درخواست تأییدشده کاربرد ندارد" : "Not applicable to an approved decision";

    public string AwaitingExecutionStatement => IsPersian
        ? "درخواست تأیید شده است؛ پرداخت موفقی ثبت نشده است."
        : "Approval was recorded; no successful transfer has been recorded.";

    public string UnsuccessfulExecutionStatement => IsPersian
        ? "تلاش ناموفق ثبت شده است؛ پرداخت موفقی ثبت نشده است."
        : "An unsuccessful attempt was recorded; no successful transfer has been recorded.";

    public string GeneratedAt(string timestamp) => IsPersian
        ? $"گزارش در {timestamp} تهیه شد."
        : $"Workbook generated at {timestamp}.";
}

internal static class PersianRialAmountInWords
{
    private static readonly string[] Ones =
    [
        "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه",
        "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"
    ];

    private static readonly string[] Tens =
        ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];

    private static readonly string[] Hundreds =
        ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];

    private static readonly string[] Scales =
        ["", "هزار", "میلیون", "میلیارد", "تریلیون", "کوادریلیون", "کوینتیلیون"];

    public static string Format(long value)
    {
        if (value == 0)
        {
            return "صفر ریال";
        }

        var negative = value < 0;
        var remaining = negative ? (ulong)(-(value + 1)) + 1 : (ulong)value;
        var groups = new List<string>();
        var scale = 0;
        while (remaining > 0)
        {
            var part = (int)(remaining % 1000);
            if (part > 0)
            {
                var words = UnderThousand(part);
                if (scale > 0)
                {
                    words += $" {Scales[scale]}";
                }
                groups.Insert(0, words);
            }
            remaining /= 1000;
            scale++;
        }

        return $"{(negative ? "منفی " : string.Empty)}{string.Join(" و ", groups)} ریال";
    }

    private static string UnderThousand(int value)
    {
        var parts = new List<string>();
        if (value >= 100)
        {
            parts.Add(Hundreds[value / 100]);
            value %= 100;
        }
        if (value is > 0 and < 20)
        {
            parts.Add(Ones[value]);
        }
        else if (value >= 20)
        {
            parts.Add(Tens[value / 10]);
            if (value % 10 > 0)
            {
                parts.Add(Ones[value % 10]);
            }
        }
        return string.Join(" و ", parts);
    }
}
