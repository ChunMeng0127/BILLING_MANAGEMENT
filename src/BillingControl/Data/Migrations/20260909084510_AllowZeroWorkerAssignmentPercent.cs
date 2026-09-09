using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowZeroWorkerAssignmentPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment",
                table: "WorkerAssignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment",
                table: "WorkerAssignments",
                sql: "\"Percent\" >= 0 AND \"Percent\" <= 100 AND \"LcmGrossSnapshot\" >= 0 AND \"Entitlement\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment",
                table: "WorkerAssignments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment",
                table: "WorkerAssignments",
                sql: "\"Percent\" > 0 AND \"Percent\" <= 100 AND \"LcmGrossSnapshot\" >= 0 AND \"Entitlement\" >= 0");
        }
    }
}
