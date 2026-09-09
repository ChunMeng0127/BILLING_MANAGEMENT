using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkerAssignmentWorkflowState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CurrentProgressPercent",
                table: "WorkerAssignments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CurrentWorkflowStatus",
                table: "WorkerAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "CurrentWorkflowVersion",
                table: "WorkerAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "WorkerAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenBy",
                table: "WorkerAssignments",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "WorkerAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReportingResumedFromWeek",
                table: "WorkerAssignments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowStatusAtSubmission",
                table: "WeeklyProgressReports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowVersionAtSubmission",
                table: "WeeklyProgressReports",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkerAssignmentWorkflowHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkerAssignmentId = table.Column<int>(type: "integer", nullable: false),
                    PreviousWorkflowStatus = table.Column<int>(type: "integer", nullable: true),
                    PreviousWorkflowVersion = table.Column<int>(type: "integer", nullable: true),
                    NewWorkflowStatus = table.Column<int>(type: "integer", nullable: false),
                    NewWorkflowVersion = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerAssignmentWorkflowHistories", x => x.Id);
                    table.CheckConstraint("CK_WorkflowHistory_NewVersion", "\"NewWorkflowVersion\" IS NULL OR \"NewWorkflowVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_WorkerAssignmentWorkflowHistories_WorkerAssignments_WorkerA~",
                        column: x => x.WorkerAssignmentId,
                        principalTable: "WorkerAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment_CurrentProgress",
                table: "WorkerAssignments",
                sql: "\"CurrentProgressPercent\" BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Assignment_CurrentWorkflowVersion",
                table: "WorkerAssignments",
                sql: "\"CurrentWorkflowVersion\" IS NULL OR \"CurrentWorkflowVersion\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WeeklyProgress_WorkflowVersion",
                table: "WeeklyProgressReports",
                sql: "\"WorkflowVersionAtSubmission\" IS NULL OR \"WorkflowVersionAtSubmission\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerAssignmentWorkflowHistories_WorkerAssignmentId_Change~",
                table: "WorkerAssignmentWorkflowHistories",
                columns: new[] { "WorkerAssignmentId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerAssignmentWorkflowHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment_CurrentProgress",
                table: "WorkerAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Assignment_CurrentWorkflowVersion",
                table: "WorkerAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WeeklyProgress_WorkflowVersion",
                table: "WeeklyProgressReports");

            migrationBuilder.DropColumn(
                name: "CurrentProgressPercent",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "CurrentWorkflowStatus",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "CurrentWorkflowVersion",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "HiddenBy",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "ReportingResumedFromWeek",
                table: "WorkerAssignments");

            migrationBuilder.DropColumn(
                name: "WorkflowStatusAtSubmission",
                table: "WeeklyProgressReports");

            migrationBuilder.DropColumn(
                name: "WorkflowVersionAtSubmission",
                table: "WeeklyProgressReports");
        }
    }
}
