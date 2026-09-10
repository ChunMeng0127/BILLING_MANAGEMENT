using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class WeeklyProgressUpdateHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyProgressUpdateHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WeeklyProgressReportId = table.Column<int>(type: "integer", nullable: false),
                    WorkerAssignmentId = table.Column<int>(type: "integer", nullable: false),
                    WorkflowStatus = table.Column<int>(type: "integer", nullable: false),
                    WorkflowVersion = table.Column<int>(type: "integer", nullable: true),
                    ProgressPercent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WorkDone = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    NextAction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IssuesOrBlockers = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyProgressUpdateHistories", x => x.Id);
                    table.CheckConstraint("CK_WeeklyProgressHistory_Percent", "\"ProgressPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_WeeklyProgressHistory_WorkflowVersion", "\"WorkflowVersion\" IS NULL OR \"WorkflowVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_WeeklyProgressUpdateHistories_WeeklyProgressReports_WeeklyP~",
                        column: x => x.WeeklyProgressReportId,
                        principalTable: "WeeklyProgressReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeeklyProgressUpdateHistories_WorkerAssignments_WorkerAssig~",
                        column: x => x.WorkerAssignmentId,
                        principalTable: "WorkerAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyProgressUpdateHistories_WeeklyProgressReportId_Occurr~",
                table: "WeeklyProgressUpdateHistories",
                columns: new[] { "WeeklyProgressReportId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyProgressUpdateHistories_WorkerAssignmentId_OccurredAt",
                table: "WeeklyProgressUpdateHistories",
                columns: new[] { "WorkerAssignmentId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeeklyProgressUpdateHistories");
        }
    }
}
