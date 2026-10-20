using Microsoft.EntityFrameworkCore.Migrations;

// Preserves existing outcomes while introducing auditable timed operational closure.
#nullable disable

namespace PurchaseApproval.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImplementTimedExecutionClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOutcomes_TransferredClosed",
                table: "TransferOutcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOutcomes_UnsuccessfulClosure",
                table: "TransferOutcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferAuditEntries_Action",
                table: "TransferAuditEntries");

            migrationBuilder.AddColumn<string>(
                name: "ClosedByRoleSnapshot",
                table: "TransferOutcomes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClosureType",
                table: "TransferOutcomes",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ActorUserId",
                table: "TransferAuditEntries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.Sql(
                """
                UPDATE "TransferOutcomes"
                SET "IsClosed" = FALSE,
                    "ClosedAtUtc" = NULL,
                    "ClosedByUserId" = NULL,
                    "ClosedByFullNameSnapshot" = NULL,
                    "ClosureReason" = NULL
                WHERE "Status" = 2;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "TransferOutcomes"
                SET "ClosureType" = 2,
                    "ClosedByRoleSnapshot" = 'Administrator'
                WHERE "Status" = 3 AND "IsClosed" = TRUE;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TransferOutcomes_IsClosed_RecordedAtUtc",
                table: "TransferOutcomes",
                columns: new[] { "IsClosed", "RecordedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOutcomes_ClosureState",
                table: "TransferOutcomes",
                sql: "(\r\n    \"IsClosed\" = FALSE\r\n    AND \"ClosedAtUtc\" IS NULL\r\n    AND \"ClosureType\" IS NULL\r\n    AND \"ClosedByUserId\" IS NULL\r\n    AND \"ClosedByFullNameSnapshot\" IS NULL\r\n    AND \"ClosedByRoleSnapshot\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"IsClosed\" = TRUE\r\n    AND \"Status\" IN (2, 3)\r\n    AND \"ClosedAtUtc\" IS NOT NULL\r\n    AND\r\n    (\r\n        (\r\n            \"ClosureType\" = 1\r\n            AND \"ClosedByUserId\" IS NULL\r\n            AND \"ClosedByFullNameSnapshot\" IS NULL\r\n            AND \"ClosedByRoleSnapshot\" IS NULL\r\n        )\r\n        OR\r\n        (\r\n            \"ClosureType\" = 2\r\n            AND \"ClosedByUserId\" IS NOT NULL\r\n            AND \"ClosedByFullNameSnapshot\" IS NOT NULL\r\n            AND \"ClosedByRoleSnapshot\" = 'Administrator'\r\n        )\r\n    )\r\n)");

            // New and updated unsuccessful outcomes require a reason without rewriting legacy data.
            migrationBuilder.Sql(
                """
                ALTER TABLE "TransferOutcomes"
                ADD CONSTRAINT "CK_TransferOutcomes_UnsuccessfulReason"
                CHECK (
                    "Status" <> 3
                    OR
                    (
                        "FailureReason" IS NOT NULL
                        AND LENGTH(BTRIM("FailureReason")) > 0
                    )
                ) NOT VALID;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferAuditEntries_Action",
                table: "TransferAuditEntries",
                sql: "\"Action\" IN (1, 2, 3, 4, 5, 6, 7)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransferOutcomes_IsClosed_RecordedAtUtc",
                table: "TransferOutcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOutcomes_ClosureState",
                table: "TransferOutcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferOutcomes_UnsuccessfulReason",
                table: "TransferOutcomes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TransferAuditEntries_Action",
                table: "TransferAuditEntries");

            migrationBuilder.DropColumn(
                name: "ClosedByRoleSnapshot",
                table: "TransferOutcomes");

            migrationBuilder.DropColumn(
                name: "ClosureType",
                table: "TransferOutcomes");

            migrationBuilder.AlterColumn<int>(
                name: "ActorUserId",
                table: "TransferAuditEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOutcomes_TransferredClosed",
                table: "TransferOutcomes",
                sql: "\"Status\" <> 2 OR \"IsClosed\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferOutcomes_UnsuccessfulClosure",
                table: "TransferOutcomes",
                sql: "\"Status\" <> 3\r\nOR \"IsClosed\" = FALSE\r\nOR\r\n(\r\n    \"ClosedAtUtc\" IS NOT NULL\r\n    AND \"ClosedByUserId\" IS NOT NULL\r\n    AND \"ClosedByFullNameSnapshot\" IS NOT NULL\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TransferAuditEntries_Action",
                table: "TransferAuditEntries",
                sql: "\"Action\" IN (1, 2, 3, 4, 5)");
        }
    }
}
