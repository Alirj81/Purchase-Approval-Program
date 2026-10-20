// Adds staff-account lifecycle fields, the self-approval indicator, transaction-time zone columns,
// mandatory closure reasons (with labelled back-fill), the generic AuditEntries table and the
// SubmissionQuotaDays counters. Existing rows are preserved; nothing is deleted.
using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PurchaseApproval.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Day6And7ReleaseHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOutcomes_ClosureState",
                table: "TransferOutcomes");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionEnteredLocalDateTime",
                table: "TransferOutcomes",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionTimeZoneId",
                table: "TransferOutcomes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureReason",
                table: "TransferAuditEntries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionEnteredLocalDateTime",
                table: "TransferAuditEntries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionTimeZoneId",
                table: "TransferAuditEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelfApproval",
                table: "ApprovalDecisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAtUtc",
                table: "ApplicationUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "ApplicationUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAtUtc",
                table: "ApplicationUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TemporaryCredentialExpiresAtUtc",
                table: "ApplicationUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    SubjectLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: true),
                    ActorFullNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorRoleSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.CheckConstraint("CK_AuditEntries_Action", "\"Action\" IN (1, 2, 3, 4, 5, 6, 7, 20, 21, 22, 23, 24, 25, 26, 40, 41, 42)");
                    table.CheckConstraint("CK_AuditEntries_Category", "\"Category\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_AuditEntries_ApplicationUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionQuotaDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequesterType = table.Column<int>(type: "integer", nullable: false),
                    RequesterId = table.Column<int>(type: "integer", nullable: false),
                    QuotaDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionQuotaDays", x => x.Id);
                    table.CheckConstraint("CK_SubmissionQuotaDays_CreatedCount", "\"CreatedCount\" >= 0");
                });

            // Back-fill, labelled as derived: the self-approval indicator for existing decisions is
            // computed from the preserved requester and decider identities. No historical audit
            // event is invented; only the persisted flag is set.
            migrationBuilder.Sql(
                """
                UPDATE "ApprovalDecisions" AS d
                SET "IsSelfApproval" = TRUE
                FROM "PaymentRequests" AS r
                WHERE r."Id" = d."PaymentRequestId"
                  AND r."RequesterType" = 2
                  AND r."ApplicationUserRequesterId" = d."DecidedByUserId";
                """);

            // Closure reasons became mandatory in this release. Rows closed before it keep their
            // outcome and closure data and receive an explicit placeholder reason so the stricter
            // check constraint below can be applied without rewriting history.
            migrationBuilder.Sql(
                """
                UPDATE "TransferOutcomes"
                SET "ClosureReason" = CASE
                    WHEN "ClosureType" = 1 AND "Status" = 2 THEN 'Automatic closure 24 hours after the transferred outcome was recorded.'
                    WHEN "ClosureType" = 1 AND "Status" = 3 THEN 'Automatic closure 48 hours after the unsuccessful outcome was recorded.'
                    ELSE 'Reason not recorded (closed before closure reasons became mandatory).'
                END
                WHERE "IsClosed" = TRUE
                  AND ("ClosureReason" IS NULL OR LENGTH(BTRIM("ClosureReason")) = 0);
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOutcomes_ClosureState",
                table: "TransferOutcomes",
                sql: "(\r\n    \"IsClosed\" = FALSE\r\n    AND \"ClosedAtUtc\" IS NULL\r\n    AND \"ClosureType\" IS NULL\r\n    AND \"ClosedByUserId\" IS NULL\r\n    AND \"ClosedByFullNameSnapshot\" IS NULL\r\n    AND \"ClosedByRoleSnapshot\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"IsClosed\" = TRUE\r\n    AND \"Status\" IN (2, 3)\r\n    AND \"ClosedAtUtc\" IS NOT NULL\r\n    AND \"ClosureReason\" IS NOT NULL\r\n    AND LENGTH(BTRIM(\"ClosureReason\")) > 0\r\n    AND\r\n    (\r\n        (\r\n            \"ClosureType\" = 1\r\n            AND \"ClosedByUserId\" IS NULL\r\n            AND \"ClosedByFullNameSnapshot\" IS NULL\r\n            AND \"ClosedByRoleSnapshot\" IS NULL\r\n        )\r\n        OR\r\n        (\r\n            \"ClosureType\" = 2\r\n            AND \"ClosedByUserId\" IS NOT NULL\r\n            AND \"ClosedByFullNameSnapshot\" IS NOT NULL\r\n            AND \"ClosedByRoleSnapshot\" = 'Administrator'\r\n        )\r\n    )\r\n)");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorUserId",
                table: "AuditEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_RecordedAtUtc",
                table: "AuditEntries",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_SubjectType_SubjectId_RecordedAtUtc",
                table: "AuditEntries",
                columns: new[] { "SubjectType", "SubjectId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionQuotaDays_RequesterType_RequesterId_QuotaDateUtc",
                table: "SubmissionQuotaDays",
                columns: new[] { "RequesterType", "RequesterId", "QuotaDateUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "SubmissionQuotaDays");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOutcomes_ClosureState",
                table: "TransferOutcomes");

            migrationBuilder.DropColumn(
                name: "TransactionEnteredLocalDateTime",
                table: "TransferOutcomes");

            migrationBuilder.DropColumn(
                name: "TransactionTimeZoneId",
                table: "TransferOutcomes");

            migrationBuilder.DropColumn(
                name: "ClosureReason",
                table: "TransferAuditEntries");

            migrationBuilder.DropColumn(
                name: "TransactionEnteredLocalDateTime",
                table: "TransferAuditEntries");

            migrationBuilder.DropColumn(
                name: "TransactionTimeZoneId",
                table: "TransferAuditEntries");

            migrationBuilder.DropColumn(
                name: "IsSelfApproval",
                table: "ApprovalDecisions");

            migrationBuilder.DropColumn(
                name: "DeactivatedAtUtc",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "TemporaryCredentialExpiresAtUtc",
                table: "ApplicationUsers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOutcomes_ClosureState",
                table: "TransferOutcomes",
                sql: "(\r\n    \"IsClosed\" = FALSE\r\n    AND \"ClosedAtUtc\" IS NULL\r\n    AND \"ClosureType\" IS NULL\r\n    AND \"ClosedByUserId\" IS NULL\r\n    AND \"ClosedByFullNameSnapshot\" IS NULL\r\n    AND \"ClosedByRoleSnapshot\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"IsClosed\" = TRUE\r\n    AND \"Status\" IN (2, 3)\r\n    AND \"ClosedAtUtc\" IS NOT NULL\r\n    AND\r\n    (\r\n        (\r\n            \"ClosureType\" = 1\r\n            AND \"ClosedByUserId\" IS NULL\r\n            AND \"ClosedByFullNameSnapshot\" IS NULL\r\n            AND \"ClosedByRoleSnapshot\" IS NULL\r\n        )\r\n        OR\r\n        (\r\n            \"ClosureType\" = 2\r\n            AND \"ClosedByUserId\" IS NOT NULL\r\n            AND \"ClosedByFullNameSnapshot\" IS NOT NULL\r\n            AND \"ClosedByRoleSnapshot\" = 'Administrator'\r\n        )\r\n    )\r\n)");
        }
    }
}
