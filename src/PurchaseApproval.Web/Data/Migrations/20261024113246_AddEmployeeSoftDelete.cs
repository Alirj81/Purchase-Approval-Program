using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseApproval.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Employees_AccessStatus",
                table: "Employees");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEntries_Action",
                table: "AuditEntries");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Employees_AccessStatus",
                table: "Employees",
                sql: "\"AccessStatus\" IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEntries_Action",
                table: "AuditEntries",
                sql: "\"Action\" IN (1, 2, 3, 4, 5, 6, 7, 20, 21, 22, 23, 24, 25, 26, 27, 40, 41, 42)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Employees_AccessStatus",
                table: "Employees");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEntries_Action",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Employees");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Employees_AccessStatus",
                table: "Employees",
                sql: "\"AccessStatus\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEntries_Action",
                table: "AuditEntries",
                sql: "\"Action\" IN (1, 2, 3, 4, 5, 6, 7, 20, 21, 22, 23, 24, 25, 26, 40, 41, 42)");
        }
    }
}
