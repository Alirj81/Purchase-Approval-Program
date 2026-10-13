using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PurchaseApproval.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAuthenticatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Branch = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AccessKeyHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CredentialVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    AccessStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAuthenticatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.CheckConstraint("CK_Employees_AccessStatus", "\"AccessStatus\" IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "ItemCatalogueEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UsageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCatalogueEntries", x => x.Id);
                    table.CheckConstraint("CK_ItemCatalogueEntries_UsageCount", "\"UsageCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReferenceNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequesterType = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    EmployeeRequesterId = table.Column<int>(type: "integer", nullable: true),
                    ApplicationUserRequesterId = table.Column<int>(type: "integer", nullable: true),
                    RequesterFullNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequesterUsernameSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequesterPhoneNumberSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RequesterDepartmentSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequesterBranchSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequesterJobTitleSnapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PayeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAccountOrCardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TotalAmountRials = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TrackingTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRequests", x => x.Id);
                    table.CheckConstraint("CK_PaymentRequests_ExactlyOneRequester", "(\r\n    \"RequesterType\" = 1\r\n    AND \"EmployeeRequesterId\" IS NOT NULL\r\n    AND \"ApplicationUserRequesterId\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"RequesterType\" = 2\r\n    AND \"EmployeeRequesterId\" IS NULL\r\n    AND \"ApplicationUserRequesterId\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_PaymentRequests_Status", "\"Status\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_PaymentRequests_TotalAmount", "\"TotalAmountRials\" > 0\r\nAND \"TotalAmountRials\" <= 10000000000");
                    table.ForeignKey(
                        name: "FK_PaymentRequests_ApplicationUsers_ApplicationUserRequesterId",
                        column: x => x.ApplicationUserRequesterId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_Employees_EmployeeRequesterId",
                        column: x => x.EmployeeRequesterId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentRequestId = table.Column<int>(type: "integer", nullable: false),
                    DecisionType = table.Column<int>(type: "integer", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: false),
                    DecidedByFullNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                    table.CheckConstraint("CK_ApprovalDecisions_DecisionType", "\"DecisionType\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_ApplicationUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_PaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "PaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequestAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentRequestId = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRequestAttachments", x => x.Id);
                    table.CheckConstraint("CK_PaymentRequestAttachments_FileSize", "\"FileSizeBytes\" > 0\r\nAND \"FileSizeBytes\" <= 5242880");
                    table.ForeignKey(
                        name: "FK_PaymentRequestAttachments_PaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "PaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentRequestId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AmountRials = table.Column<long>(type: "bigint", nullable: false),
                    HasInvoice = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRequestLines", x => x.Id);
                    table.CheckConstraint("CK_PaymentRequestLines_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_PaymentRequestLines_PositiveAmount", "\"AmountRials\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentRequestLines_PaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "PaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferOutcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentRequestId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: true),
                    OtherMethodDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DestinationAccountOrCardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ChequeNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChequeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChequePayeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PerformedByFullNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PerformedByRoleSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TransactionOccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionUtcOffsetMinutes = table.Column<short>(type: "smallint", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ClosedByFullNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ClosureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferOutcomes", x => x.Id);
                    table.CheckConstraint("CK_TransferOutcomes_BankTransferDetails", "\"Method\" IS DISTINCT FROM 2\r\nOR\r\n(\r\n    \"DestinationAccountOrCardNumber\" IS NOT NULL\r\n    AND LENGTH(\r\n        BTRIM(\"DestinationAccountOrCardNumber\")\r\n    ) > 0\r\n)");
                    table.CheckConstraint("CK_TransferOutcomes_ChequeDetails", "\"Method\" IS DISTINCT FROM 3\r\nOR\r\n(\r\n    \"ChequeNumber\" IS NOT NULL\r\n    AND \"ChequeDate\" IS NOT NULL\r\n    AND \"BankName\" IS NOT NULL\r\n    AND \"ChequePayeeName\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_TransferOutcomes_Method", "\"Method\" IS NULL OR \"Method\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_TransferOutcomes_MethodByStatus", "(\r\n    \"Status\" = 1\r\n    AND \"Method\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"Status\" IN (2, 3)\r\n    AND \"Method\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_TransferOutcomes_OtherMethodDetails", "\"Method\" IS DISTINCT FROM 4\r\nOR\r\n(\r\n    \"OtherMethodDescription\" IS NOT NULL\r\n    AND LENGTH(\r\n        BTRIM(\"OtherMethodDescription\")\r\n    ) > 0\r\n)");
                    table.CheckConstraint("CK_TransferOutcomes_Performer", "(\r\n    \"Status\" = 1\r\n    AND \"PerformedByUserId\" IS NULL\r\n    AND \"TransactionOccurredAtUtc\" IS NULL\r\n    AND \"TransactionUtcOffsetMinutes\" IS NULL\r\n    AND \"RecordedAtUtc\" IS NULL\r\n    AND \"IsClosed\" = FALSE\r\n)\r\nOR\r\n(\r\n    \"Status\" IN (2, 3)\r\n    AND \"PerformedByUserId\" IS NOT NULL\r\n    AND \"PerformedByFullNameSnapshot\" IS NOT NULL\r\n    AND \"PerformedByRoleSnapshot\" IS NOT NULL\r\n    AND \"TransactionOccurredAtUtc\" IS NOT NULL\r\n    AND \"TransactionUtcOffsetMinutes\" IS NOT NULL\r\n    AND \"RecordedAtUtc\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_TransferOutcomes_Status", "\"Status\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_TransferOutcomes_TransferredClosed", "\"Status\" <> 2 OR \"IsClosed\" = TRUE");
                    table.CheckConstraint("CK_TransferOutcomes_UnsuccessfulClosure", "\"Status\" <> 3\r\nOR \"IsClosed\" = FALSE\r\nOR\r\n(\r\n    \"ClosedAtUtc\" IS NOT NULL\r\n    AND \"ClosedByUserId\" IS NOT NULL\r\n    AND \"ClosedByFullNameSnapshot\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_TransferOutcomes_UtcOffset", "\"TransactionUtcOffsetMinutes\" IS NULL\r\nOR \"TransactionUtcOffsetMinutes\"\r\n    BETWEEN -840 AND 840");
                    table.ForeignKey(
                        name: "FK_TransferOutcomes_ApplicationUsers_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOutcomes_ApplicationUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferOutcomes_PaymentRequests_PaymentRequestId",
                        column: x => x.PaymentRequestId,
                        principalTable: "PaymentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferAuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransferOutcomeId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    StatusAfterAction = table.Column<int>(type: "integer", nullable: false),
                    IsClosedAfterAction = table.Column<bool>(type: "boolean", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: true),
                    OtherMethodDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DestinationAccountOrCardNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ChequeNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChequeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChequePayeeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActorFullNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorRoleSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TransactionOccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionUtcOffsetMinutes = table.Column<short>(type: "smallint", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferAuditEntries", x => x.Id);
                    table.CheckConstraint("CK_TransferAuditEntries_Action", "\"Action\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_TransferAuditEntries_BankTransferDetails", "\"Method\" IS DISTINCT FROM 2\r\nOR\r\n(\r\n    \"DestinationAccountOrCardNumber\" IS NOT NULL\r\n    AND LENGTH(\r\n        BTRIM(\"DestinationAccountOrCardNumber\")\r\n    ) > 0\r\n)");
                    table.CheckConstraint("CK_TransferAuditEntries_ChequeDetails", "\"Method\" IS DISTINCT FROM 3\r\nOR\r\n(\r\n    \"ChequeNumber\" IS NOT NULL\r\n    AND \"ChequeDate\" IS NOT NULL\r\n    AND \"BankName\" IS NOT NULL\r\n    AND \"ChequePayeeName\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_TransferAuditEntries_Method", "\"Method\" IS NULL OR \"Method\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_TransferAuditEntries_MethodByStatus", "(\r\n    \"StatusAfterAction\" = 1\r\n    AND \"Method\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"StatusAfterAction\" IN (2, 3)\r\n    AND \"Method\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_TransferAuditEntries_OtherMethodDetails", "\"Method\" IS DISTINCT FROM 4\r\nOR\r\n(\r\n    \"OtherMethodDescription\" IS NOT NULL\r\n    AND LENGTH(\r\n        BTRIM(\"OtherMethodDescription\")\r\n    ) > 0\r\n)");
                    table.CheckConstraint("CK_TransferAuditEntries_Status", "\"StatusAfterAction\" IN (1, 2, 3)");
                    table.CheckConstraint("CK_TransferAuditEntries_UtcOffset", "\"TransactionUtcOffsetMinutes\" IS NULL\r\nOR \"TransactionUtcOffsetMinutes\"\r\n    BETWEEN -840 AND 840");
                    table.ForeignKey(
                        name: "FK_TransferAuditEntries_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferAuditEntries_TransferOutcomes_TransferOutcomeId",
                        column: x => x.TransferOutcomeId,
                        principalTable: "TransferOutcomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "ApplicationUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "ApplicationUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_DecidedAtUtc",
                table: "ApprovalDecisions",
                column: "DecidedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_DecidedByUserId",
                table: "ApprovalDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_PaymentRequestId",
                table: "ApprovalDecisions",
                column: "PaymentRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_AccessStatus",
                table: "Employees",
                column: "AccessStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NormalizedUsername",
                table: "Employees",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemCatalogueEntries_IsActive_UsageCount",
                table: "ItemCatalogueEntries",
                columns: new[] { "IsActive", "UsageCount" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemCatalogueEntries_NormalizedName",
                table: "ItemCatalogueEntries",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequestAttachments_PaymentRequestId",
                table: "PaymentRequestAttachments",
                column: "PaymentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequestLines_PaymentRequestId_DisplayOrder",
                table: "PaymentRequestLines",
                columns: new[] { "PaymentRequestId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_ApplicationUserRequesterId",
                table: "PaymentRequests",
                column: "ApplicationUserRequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_EmployeeRequesterId",
                table: "PaymentRequests",
                column: "EmployeeRequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_ReferenceNumber",
                table: "PaymentRequests",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Status",
                table: "PaymentRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_SubmittedAtUtc",
                table: "PaymentRequests",
                column: "SubmittedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_TrackingTokenHash",
                table: "PaymentRequests",
                column: "TrackingTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferAuditEntries_ActorUserId",
                table: "TransferAuditEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferAuditEntries_TransferOutcomeId_RecordedAtUtc",
                table: "TransferAuditEntries",
                columns: new[] { "TransferOutcomeId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferOutcomes_ClosedByUserId",
                table: "TransferOutcomes",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOutcomes_PaymentRequestId",
                table: "TransferOutcomes",
                column: "PaymentRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOutcomes_PerformedByUserId",
                table: "TransferOutcomes",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferOutcomes_Status_IsClosed",
                table: "TransferOutcomes",
                columns: new[] { "Status", "IsClosed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDecisions");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "ItemCatalogueEntries");

            migrationBuilder.DropTable(
                name: "PaymentRequestAttachments");

            migrationBuilder.DropTable(
                name: "PaymentRequestLines");

            migrationBuilder.DropTable(
                name: "TransferAuditEntries");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "TransferOutcomes");

            migrationBuilder.DropTable(
                name: "PaymentRequests");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
