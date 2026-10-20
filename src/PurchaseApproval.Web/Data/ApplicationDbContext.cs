// Identity types define the administrator and accountant tables in this context.
using Microsoft.AspNetCore.Identity;
// Connects Identity stores to Entity Framework Core.
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
// Provides DbContext, model configuration, and PostgreSQL query support.
using Microsoft.EntityFrameworkCore;
using PurchaseApproval.Models;

namespace PurchaseApproval.Data;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(
    ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    ConfigureApplicationUsers(modelBuilder);
    ConfigureEmployees(modelBuilder);
    ConfigurePaymentRequests(modelBuilder);
    ConfigurePaymentRequestLines(modelBuilder);
    ConfigurePaymentRequestAttachments(modelBuilder);
    ConfigureApprovalDecisions(modelBuilder);
    ConfigureTransferOutcomes(modelBuilder);
    ConfigureTransferAuditEntries(modelBuilder);
    ConfigureItemCatalogueEntries(modelBuilder);
    ConfigureAuditEntries(modelBuilder);
    ConfigureSubmissionQuotaDays(modelBuilder);
}

// Generic accountability trail for staff accounts, employee access and approval decisions.
// Transfer execution keeps its own richer TransferAuditEntries table.
private static void ConfigureAuditEntries(ModelBuilder modelBuilder)
{
    var audit = modelBuilder.Entity<AuditEntry>();

    audit.ToTable(
        "AuditEntries",
        table =>
        {
            table.HasCheckConstraint(
                "CK_AuditEntries_Category",
                """
                "Category" IN (1, 2, 3)
                """);

            table.HasCheckConstraint(
                "CK_AuditEntries_Action",
                """
                "Action" IN (1, 2, 3, 4, 5, 6, 7, 20, 21, 22, 23, 24, 25, 26, 40, 41, 42)
                """);
        });

    audit.Property(item => item.Category).HasConversion<int>().IsRequired();
    audit.Property(item => item.Action).HasConversion<int>().IsRequired();
    audit.Property(item => item.SubjectType).HasMaxLength(50).IsRequired();
    audit.Property(item => item.SubjectLabel).HasMaxLength(200).IsRequired();
    audit.Property(item => item.ActorFullNameSnapshot).HasMaxLength(200).IsRequired();
    audit.Property(item => item.ActorRoleSnapshot).HasMaxLength(50).IsRequired();
    audit.Property(item => item.Details).HasMaxLength(2000);

    audit
        .HasOne(item => item.ActorUser)
        .WithMany()
        .HasForeignKey(item => item.ActorUserId)
        .OnDelete(DeleteBehavior.Restrict);

    audit.HasIndex(item => new { item.SubjectType, item.SubjectId, item.RecordedAtUtc });
    audit.HasIndex(item => item.RecordedAtUtc);
}

// Daily submission quota counters. The unique index is what makes concurrent first
// submissions on the same UTC day collapse into one row (see SubmissionQuotaService).
private static void ConfigureSubmissionQuotaDays(ModelBuilder modelBuilder)
{
    var quota = modelBuilder.Entity<SubmissionQuotaDay>();

    quota.ToTable(
        "SubmissionQuotaDays",
        table => table.HasCheckConstraint(
            "CK_SubmissionQuotaDays_CreatedCount",
            """
            "CreatedCount" >= 0
            """));

    quota.Property(item => item.RequesterType).HasConversion<int>().IsRequired();
    quota.Property(item => item.QuotaDateUtc).HasColumnType("date");
    quota.Property(item => item.Version).IsRowVersion();

    quota
        .HasIndex(item => new { item.RequesterType, item.RequesterId, item.QuotaDateUtc })
        .IsUnique();
}
private static void ConfigurePaymentRequestAttachments(ModelBuilder modelBuilder)
{
    var attachment =
        modelBuilder.Entity<PaymentRequestAttachment>();

    attachment.ToTable(
        "PaymentRequestAttachments",
        table =>
        {
            table.HasCheckConstraint(
                "CK_PaymentRequestAttachments_FileSize",
                """
                "FileSizeBytes" > 0
                AND "FileSizeBytes" <= 5242880
                """);
        });

    attachment
        .Property(item => item.OriginalFileName)
        .HasMaxLength(255)
        .IsRequired();

    attachment
        .Property(item => item.ContentType)
        .HasMaxLength(100)
        .IsRequired();

    attachment
        .Property(item => item.Sha256Hash)
        .HasMaxLength(64)
        .IsRequired();

    attachment
        .Property(item => item.Content)
        .HasColumnType("bytea")
        .IsRequired();

    attachment
        .HasOne(item => item.PaymentRequest)
        .WithMany(item => item.Attachments)
        .HasForeignKey(item => item.PaymentRequestId)
        .OnDelete(DeleteBehavior.Cascade);

    attachment
        .HasIndex(item => item.PaymentRequestId);
}
private static void ConfigurePaymentRequestLines(ModelBuilder modelBuilder)
{
    var line = modelBuilder.Entity<PaymentRequestLine>();

    line.ToTable(
        "PaymentRequestLines",
        table =>
        {
            table.HasCheckConstraint(
                "CK_PaymentRequestLines_PositiveAmount",
                """
                "AmountRials" > 0
                """);

            table.HasCheckConstraint(
                "CK_PaymentRequestLines_DisplayOrder",
                """
                "DisplayOrder" >= 0
                """);
        });

    line
        .Property(item => item.Description)
        .HasMaxLength(500)
        .IsRequired();

    line
        .HasOne(item => item.PaymentRequest)
        .WithMany(item => item.Lines)
        .HasForeignKey(item => item.PaymentRequestId)
        .OnDelete(DeleteBehavior.Cascade);

    line
        .HasIndex(item => new
        {
            item.PaymentRequestId,
            item.DisplayOrder
        })
        .IsUnique();
    line
        .Property(item => item.HasInvoice)
        .HasDefaultValue(false);

    line
        .Property(item => item.Remarks)
        .HasMaxLength(1000);
}
private static void ConfigurePaymentRequests(ModelBuilder modelBuilder)
{
    var request = modelBuilder.Entity<PaymentRequest>();

    request.ToTable(
        "PaymentRequests",
        table =>
        {
            table.HasCheckConstraint(
                "CK_PaymentRequests_ExactlyOneRequester",
                """
                (
                    "RequesterType" = 1
                    AND "EmployeeRequesterId" IS NOT NULL
                    AND "ApplicationUserRequesterId" IS NULL
                )
                OR
                (
                    "RequesterType" = 2
                    AND "EmployeeRequesterId" IS NULL
                    AND "ApplicationUserRequesterId" IS NOT NULL
                )
                """);
            table.HasCheckConstraint(
                "CK_PaymentRequests_Status",
                """
                "Status" IN (1, 2, 3)
                """);

            table.HasCheckConstraint(
                "CK_PaymentRequests_TotalAmount",
                """
                "TotalAmountRials" > 0
                AND "TotalAmountRials" <= 10000000000
                """);
        });
    request
    .Property(item => item.Version)
    .IsRowVersion();

    request
        .Property(item => item.ReferenceNumber)
        .HasMaxLength(30)
        .IsRequired();

    request
        .HasIndex(item => item.ReferenceNumber)
        .IsUnique();

    request
        .Property(item => item.RequesterType)
        .HasConversion<int>()
        .IsRequired();

    request
        .HasOne(item => item.EmployeeRequester)
        .WithMany()
        .HasForeignKey(item => item.EmployeeRequesterId)
        .OnDelete(DeleteBehavior.Restrict);

    request
        .HasOne(item => item.ApplicationUserRequester)
        .WithMany()
        .HasForeignKey(item => item.ApplicationUserRequesterId)
        .OnDelete(DeleteBehavior.Restrict);

    request
        .Property(item => item.RequesterFullNameSnapshot)
        .HasMaxLength(200)
        .IsRequired();

    request
        .Property(item => item.RequesterUsernameSnapshot)
        .HasMaxLength(100)
        .IsRequired();

    request
        .Property(item => item.RequesterPhoneNumberSnapshot)
        .HasMaxLength(50);

    request
        .Property(item => item.RequesterDepartmentSnapshot)
        .HasMaxLength(100);

    request
        .Property(item => item.RequesterBranchSnapshot)
        .HasMaxLength(100);

    request
        .Property(item => item.RequesterJobTitleSnapshot)
        .HasMaxLength(100);

    request
        .Property(item => item.Justification)
        .HasMaxLength(2000)
        .IsRequired();

    request
        .Property(item => item.Status)
        .HasConversion<int>();
    request
        .Property(item => item.TrackingTokenHash)
        .HasMaxLength(128)
        .IsRequired();

    request
        .HasIndex(item => item.TrackingTokenHash)
        .IsUnique();

    request
        .Property(item => item.SubmissionKeyHash)
        .HasMaxLength(64);

    request
        .HasIndex(item => item.SubmissionKeyHash)
        .IsUnique();

    request
        .HasIndex(item => item.Status);

    request
        .HasIndex(item => item.SubmittedAtUtc);
    request
        .Property(item => item.PayeeName)
        .HasMaxLength(200)
        .IsRequired();

    request
        .Property(item => item.RequestedAccountOrCardNumber)
        .HasMaxLength(100);
}

    private static void ConfigureApplicationUsers(
    ModelBuilder modelBuilder)
{
    var user = modelBuilder.Entity<ApplicationUser>();

    user.ToTable("ApplicationUsers");

    user
        .Property(item => item.FullName)
        .HasMaxLength(200)
        .IsRequired();

    user
        .Property(item => item.Department)
        .HasMaxLength(100);

    user
        .Property(item => item.Branch)
        .HasMaxLength(100);

    user
        .Property(item => item.JobTitle)
        .HasMaxLength(100);

    user
        .Property(item => item.IsActive)
        .HasDefaultValue(true);

    user
        .Property(item => item.MustChangePassword)
        .HasDefaultValue(false);
}

    private static void ConfigureEmployees(ModelBuilder modelBuilder) // Configure the Employee entity
    {
        var employee = modelBuilder.Entity<Employee>();

        employee.ToTable(
        "Employees",
        table =>
        {
            table.HasCheckConstraint(
                "CK_Employees_AccessStatus",
                """
                "AccessStatus" IN (1, 2, 3)
                """);
        });

        employee
            .Property(item => item.FullName)
            .HasMaxLength(200)
            .IsRequired();

        employee
            .Property(item => item.Username)
            .HasMaxLength(100)
            .IsRequired();

        employee
            .Property(item => item.NormalizedUsername)
            .HasMaxLength(100)
            .IsRequired();

        employee
            .HasIndex(item => item.NormalizedUsername)
            .IsUnique();

        employee
            .Property(item => item.PhoneNumber)
            .HasMaxLength(50);

        employee
            .Property(item => item.Department)
            .HasMaxLength(100);

        employee
            .Property(item => item.Branch)
            .HasMaxLength(100);

        employee
            .Property(item => item.JobTitle)
            .HasMaxLength(100);

        employee
            .Property(item => item.AdminNotes)
            .HasMaxLength(1000);

        employee
            .Property(item => item.AccessKeyHash)
            .HasMaxLength(500)
            .IsRequired();

        employee
            .Property(item => item.CredentialVersion)
            .HasDefaultValue(1);

        employee
            .Property(item => item.AccessStatus)
            .HasConversion<int>();

        employee
            .HasIndex(item => item.AccessStatus);
    }
    private static void ConfigureApprovalDecisions(
    ModelBuilder modelBuilder)
{
    var decision = modelBuilder.Entity<ApprovalDecision>();

    decision.ToTable(
        "ApprovalDecisions",
        table =>
        {
            table.HasCheckConstraint(
                "CK_ApprovalDecisions_DecisionType",
                """
                "DecisionType" IN (1, 2)
                """);
        });

    decision
        .Property(item => item.DecisionType)
        .HasConversion<int>()
        .IsRequired();

    decision
        .Property(item => item.DecidedByFullNameSnapshot)
        .HasMaxLength(200)
        .IsRequired();

    decision
        .Property(item => item.RejectionReason)
        .HasMaxLength(2000);

    decision
        .Property(item => item.IsSelfApproval)
        .HasDefaultValue(false);

    decision
        .HasOne(item => item.PaymentRequest)
        .WithOne(item => item.Decision)
        .HasForeignKey<ApprovalDecision>(
            item => item.PaymentRequestId)
        .OnDelete(DeleteBehavior.Cascade);

    decision
        .HasOne(item => item.DecidedByUser)
        .WithMany()
        .HasForeignKey(item => item.DecidedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    decision
        .HasIndex(item => item.PaymentRequestId)
        .IsUnique();

    decision
        .HasIndex(item => item.DecidedAtUtc);

    decision
        .HasIndex(item => item.DecidedByUserId);
}
private static void ConfigureTransferOutcomes(
    ModelBuilder modelBuilder)
{
    var transfer = modelBuilder.Entity<TransferOutcome>();

    transfer.ToTable(
        "TransferOutcomes",
        table =>
        {
            table.HasCheckConstraint(
                "CK_TransferOutcomes_Status",
                """
                "Status" IN (1, 2, 3)
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_Performer",
                """
                (
                    "Status" = 1
                    AND "PerformedByUserId" IS NULL
                    AND "TransactionOccurredAtUtc" IS NULL
                    AND "TransactionUtcOffsetMinutes" IS NULL
                    AND "RecordedAtUtc" IS NULL
                    AND "IsClosed" = FALSE
                )
                OR
                (
                    "Status" IN (2, 3)
                    AND "PerformedByUserId" IS NOT NULL
                    AND "PerformedByFullNameSnapshot" IS NOT NULL
                    AND "PerformedByRoleSnapshot" IS NOT NULL
                    AND "TransactionOccurredAtUtc" IS NOT NULL
                    AND "TransactionUtcOffsetMinutes" IS NOT NULL
                    AND "RecordedAtUtc" IS NOT NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_UnsuccessfulReason",
                """
                "Status" <> 3
                OR
                (
                    "FailureReason" IS NOT NULL
                    AND LENGTH(BTRIM("FailureReason")) > 0
                )
                """);
            table.HasCheckConstraint(
                "CK_TransferOutcomes_Method",
                """
                "Method" IS NULL OR "Method" IN (1, 2, 3, 4)
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_MethodByStatus",
                """
                (
                    "Status" = 1
                    AND "Method" IS NULL
                )
                OR
                (
                    "Status" IN (2, 3)
                    AND "Method" IS NOT NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_BankTransferDetails",
                """
                "Method" IS DISTINCT FROM 2
                OR
                (
                    "DestinationAccountOrCardNumber" IS NOT NULL
                    AND LENGTH(
                        BTRIM("DestinationAccountOrCardNumber")
                    ) > 0
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_ChequeDetails",
                """
                "Method" IS DISTINCT FROM 3
                OR
                (
                    "ChequeNumber" IS NOT NULL
                    AND "ChequeDate" IS NOT NULL
                    AND "BankName" IS NOT NULL
                    AND "ChequePayeeName" IS NOT NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_OtherMethodDetails",
                """
                "Method" IS DISTINCT FROM 4
                OR
                (
                    "OtherMethodDescription" IS NOT NULL
                    AND LENGTH(
                        BTRIM("OtherMethodDescription")
                    ) > 0
                )
                """);
            table.HasCheckConstraint(
                "CK_TransferOutcomes_ClosureState",
                """
                (
                    "IsClosed" = FALSE
                    AND "ClosedAtUtc" IS NULL
                    AND "ClosureType" IS NULL
                    AND "ClosedByUserId" IS NULL
                    AND "ClosedByFullNameSnapshot" IS NULL
                    AND "ClosedByRoleSnapshot" IS NULL
                )
                OR
                (
                    "IsClosed" = TRUE
                    AND "Status" IN (2, 3)
                    AND "ClosedAtUtc" IS NOT NULL
                    AND "ClosureReason" IS NOT NULL
                    AND LENGTH(BTRIM("ClosureReason")) > 0
                    AND
                    (
                        (
                            "ClosureType" = 1
                            AND "ClosedByUserId" IS NULL
                            AND "ClosedByFullNameSnapshot" IS NULL
                            AND "ClosedByRoleSnapshot" IS NULL
                        )
                        OR
                        (
                            "ClosureType" = 2
                            AND "ClosedByUserId" IS NOT NULL
                            AND "ClosedByFullNameSnapshot" IS NOT NULL
                            AND "ClosedByRoleSnapshot" = 'Administrator'
                        )
                    )
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferOutcomes_UtcOffset",
                """
                "TransactionUtcOffsetMinutes" IS NULL
                OR "TransactionUtcOffsetMinutes"
                    BETWEEN -840 AND 840
                """);
        });

    transfer
        .Property(item => item.Status)
        .HasConversion<int>();

    transfer
        .Property(item => item.PerformedByFullNameSnapshot)
        .HasMaxLength(200);

    transfer
        .Property(item => item.PerformedByRoleSnapshot)
        .HasMaxLength(50);

    transfer
        .Property(item => item.FailureReason)
        .HasMaxLength(2000);

    transfer
        .Property(item => item.ExternalReference)
        .HasMaxLength(200);

    transfer
        .Property(item => item.ClosedByFullNameSnapshot)
        .HasMaxLength(200);

    transfer
        .Property(item => item.ClosedByRoleSnapshot)
        .HasMaxLength(50);

    transfer
        .Property(item => item.ClosureType)
        .HasConversion<int>();

    transfer
        .Property(item => item.ClosureReason)
        .HasMaxLength(2000);

    transfer
        .Property(item => item.TransactionTimeZoneId)
        .HasMaxLength(100);

    // "timestamp without time zone" stores the wall-clock entry exactly as typed; the UTC
    // instant and offset columns carry the resolved meaning.
    transfer
        .Property(item => item.TransactionEnteredLocalDateTime)
        .HasColumnType("timestamp without time zone");

    transfer
        .Property(item => item.Version)
        .IsRowVersion();

    transfer
        .HasOne(item => item.PaymentRequest)
        .WithOne(item => item.TransferOutcome)
        .HasForeignKey<TransferOutcome>(
            item => item.PaymentRequestId)
        .OnDelete(DeleteBehavior.Cascade);

    transfer
        .HasOne(item => item.PerformedByUser)
        .WithMany()
        .HasForeignKey(item => item.PerformedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    transfer
        .HasOne(item => item.ClosedByUser)
        .WithMany()
        .HasForeignKey(item => item.ClosedByUserId)
        .OnDelete(DeleteBehavior.Restrict);

    transfer
        .HasIndex(item => item.PaymentRequestId)
        .IsUnique();

    transfer
        .HasIndex(item => new
        {
            item.Status,
            item.IsClosed
        });

    transfer
        .HasIndex(item => new
        {
            item.IsClosed,
            item.RecordedAtUtc
        });
        transfer
    .Property(item => item.Method)
    .HasConversion<int>();

    transfer
        .Property(item => item.OtherMethodDescription)
        .HasMaxLength(500);

    transfer
        .Property(item => item.DestinationAccountOrCardNumber)
        .HasMaxLength(100);

    transfer
        .Property(item => item.BankName)
        .HasMaxLength(200);

    transfer
        .Property(item => item.ChequeNumber)
        .HasMaxLength(100);

    transfer
        .Property(item => item.ChequeDate)
        .HasColumnType("date");

    transfer
        .Property(item => item.ChequePayeeName)
        .HasMaxLength(200);
}
private static void ConfigureTransferAuditEntries(
    ModelBuilder modelBuilder)
{
    var audit = modelBuilder.Entity<TransferAuditEntry>();

    audit.ToTable(
        "TransferAuditEntries",
        table =>
        {
            table.HasCheckConstraint(
                "CK_TransferAuditEntries_Action",
                """
                "Action" IN (1, 2, 3, 4, 5, 6, 7)
                """);

            table.HasCheckConstraint(
                "CK_TransferAuditEntries_Status",
                """
                "StatusAfterAction" IN (1, 2, 3)
                """);

            table.HasCheckConstraint(
                "CK_TransferAuditEntries_UtcOffset",
                """
                "TransactionUtcOffsetMinutes" IS NULL
                OR "TransactionUtcOffsetMinutes"
                    BETWEEN -840 AND 840
                """);
            table.HasCheckConstraint(
            "CK_TransferAuditEntries_Method",
            """
            "Method" IS NULL OR "Method" IN (1, 2, 3, 4)
            """);

            table.HasCheckConstraint(
                "CK_TransferAuditEntries_MethodByStatus",
                """
                (
                    "StatusAfterAction" = 1
                    AND "Method" IS NULL
                )
                OR
                (
                    "StatusAfterAction" IN (2, 3)
                    AND "Method" IS NOT NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferAuditEntries_BankTransferDetails",
                """
                "Method" IS DISTINCT FROM 2
                OR
                (
                    "DestinationAccountOrCardNumber" IS NOT NULL
                    AND LENGTH(
                        BTRIM("DestinationAccountOrCardNumber")
                    ) > 0
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferAuditEntries_ChequeDetails",
                """
                "Method" IS DISTINCT FROM 3
                OR
                (
                    "ChequeNumber" IS NOT NULL
                    AND "ChequeDate" IS NOT NULL
                    AND "BankName" IS NOT NULL
                    AND "ChequePayeeName" IS NOT NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_TransferAuditEntries_OtherMethodDetails",
                """
                "Method" IS DISTINCT FROM 4
                OR
                (
                    "OtherMethodDescription" IS NOT NULL
                    AND LENGTH(
                        BTRIM("OtherMethodDescription")
                    ) > 0
                )
                """);
            });
    audit
        .Property(item => item.Method)
        .HasConversion<int>();

    audit
        .Property(item => item.OtherMethodDescription)
        .HasMaxLength(500);

    audit
        .Property(item => item.DestinationAccountOrCardNumber)
        .HasMaxLength(100);

    audit
        .Property(item => item.BankName)
        .HasMaxLength(200);

    audit
        .Property(item => item.ChequeNumber)
        .HasMaxLength(100);

    audit
        .Property(item => item.ChequeDate)
        .HasColumnType("date");

    audit
        .Property(item => item.ChequePayeeName)
        .HasMaxLength(200);

    audit
        .Property(item => item.Action)
        .HasConversion<int>()
        .IsRequired();

    audit
        .Property(item => item.StatusAfterAction)
        .HasConversion<int>()
        .IsRequired();

    audit
        .Property(item => item.ActorFullNameSnapshot)
        .HasMaxLength(200)
        .IsRequired();

    audit
        .Property(item => item.ActorRoleSnapshot)
        .HasMaxLength(50)
        .IsRequired();

    audit
        .Property(item => item.FailureReason)
        .HasMaxLength(2000);

    audit
        .Property(item => item.ExternalReference)
        .HasMaxLength(200);

    audit
        .Property(item => item.Notes)
        .HasMaxLength(2000);

    audit
        .Property(item => item.ClosureReason)
        .HasMaxLength(2000);

    audit
        .Property(item => item.TransactionTimeZoneId)
        .HasMaxLength(100);

    audit
        .Property(item => item.TransactionEnteredLocalDateTime)
        .HasColumnType("timestamp without time zone");

    audit
        .HasOne(item => item.TransferOutcome)
        .WithMany(item => item.AuditEntries)
        .HasForeignKey(item => item.TransferOutcomeId)
        .OnDelete(DeleteBehavior.Cascade);

    audit
        .HasOne(item => item.ActorUser)
        .WithMany()
        .HasForeignKey(item => item.ActorUserId)
        .OnDelete(DeleteBehavior.Restrict);

    audit
        .HasIndex(item => new
        {
            item.TransferOutcomeId,
            item.RecordedAtUtc
        });

    audit
        .HasIndex(item => item.ActorUserId);
}
private static void ConfigureItemCatalogueEntries(
    ModelBuilder modelBuilder)
{
    var entry =
        modelBuilder.Entity<ItemCatalogueEntry>();

    entry.ToTable(
        "ItemCatalogueEntries",
        table =>
        {
            table.HasCheckConstraint(
                "CK_ItemCatalogueEntries_UsageCount",
                """
                "UsageCount" >= 0
                """);
        });

    entry
        .Property(item => item.Name)
        .HasMaxLength(500)
        .IsRequired();

    entry
        .Property(item => item.NormalizedName)
        .HasMaxLength(500)
        .IsRequired();

    entry
        .Property(item => item.IsActive)
        .HasDefaultValue(true);

    entry
        .Property(item => item.UsageCount)
        .HasDefaultValue(0);

    entry
        .HasIndex(item => item.NormalizedName)
        .IsUnique();

    entry
        .HasIndex(item => new
        {
            item.IsActive,
            item.UsageCount
        });
}


public DbSet<PaymentRequest> PaymentRequests
    => Set<PaymentRequest>();

public DbSet<PaymentRequestLine> PaymentRequestLines
    => Set<PaymentRequestLine>();

public DbSet<PaymentRequestAttachment> PaymentRequestAttachments
    => Set<PaymentRequestAttachment>();
    public DbSet<ApprovalDecision> ApprovalDecisions
    => Set<ApprovalDecision>(); // Configure the ApprovalDecision entity in the DbContext
    public DbSet<TransferOutcome> TransferOutcomes
    => Set<TransferOutcome>(); // Configure the TransferOutcome entity in the DbContext
    public DbSet<TransferAuditEntry> TransferAuditEntries 
    => Set<TransferAuditEntry>(); // Configure the TransferAuditEntry entity in the DbContext
    public DbSet<ItemCatalogueEntry> ItemCatalogueEntries
    => Set<ItemCatalogueEntry>();
    public DbSet<AuditEntry> AuditEntries
    => Set<AuditEntry>();
    public DbSet<SubmissionQuotaDay> SubmissionQuotaDays
    => Set<SubmissionQuotaDay>();
}
