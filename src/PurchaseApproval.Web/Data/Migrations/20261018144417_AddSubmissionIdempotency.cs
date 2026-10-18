using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseApproval.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmissionKeyHash",
                table: "PaymentRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_SubmissionKeyHash",
                table: "PaymentRequests",
                column: "SubmissionKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentRequests_SubmissionKeyHash",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "SubmissionKeyHash",
                table: "PaymentRequests");
        }
    }
}
