using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevenueShareBaseAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RevenueShareBaseAmount",
                table: "BillingRecords",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Existing billing records used their customer amount as the share base.
            // Backfill inside the migration before making the historical snapshot required.
            migrationBuilder.Sql("UPDATE \"BillingRecords\" SET \"RevenueShareBaseAmount\" = \"Amount\";");

            migrationBuilder.AlterColumn<decimal>(
                name: "RevenueShareBaseAmount",
                table: "BillingRecords",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Billing_RevenueShareBaseAmount",
                table: "BillingRecords",
                sql: "\"RevenueShareBaseAmount\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Billing_RevenueShareBaseAmount",
                table: "BillingRecords");

            migrationBuilder.DropColumn(
                name: "RevenueShareBaseAmount",
                table: "BillingRecords");
        }
    }
}
