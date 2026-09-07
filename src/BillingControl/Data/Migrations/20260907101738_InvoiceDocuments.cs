using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Flow = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    BusinessPartyId = table.Column<int>(type: "integer", nullable: true),
                    ManagerId = table.Column<int>(type: "integer", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoice_Party", "(\"Flow\" = 0 AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NULL) OR (\"Flow\" = 1 AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NOT NULL) OR (\"Flow\" = 2 AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NOT NULL)");
                    table.CheckConstraint("CK_Invoice_Total", "\"Total\" > 0");
                    table.ForeignKey(
                        name: "FK_Invoices_BusinessParties_BusinessPartyId",
                        column: x => x.BusinessPartyId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerReceiptId = table.Column<int>(type: "integer", nullable: false),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceiptAllocations", x => x.Id);
                    table.CheckConstraint("CK_ReceiptAllocation_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_CustomerReceipts_CustomerReceipt~",
                        column: x => x.CustomerReceiptId,
                        principalTable: "CustomerReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    BillingRecordId = table.Column<int>(type: "integer", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_InvoiceLine_Amount", "\"AllocatedAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_InvoiceLines_BillingRecords_BillingRecordId",
                        column: x => x.BillingRecordId,
                        principalTable: "BillingRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_CustomerReceiptId_InvoiceId",
                table: "CustomerReceiptAllocations",
                columns: new[] { "CustomerReceiptId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_InvoiceId",
                table: "CustomerReceiptAllocations",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_BillingRecordId",
                table: "InvoiceLines",
                column: "BillingRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId_BillingRecordId",
                table: "InvoiceLines",
                columns: new[] { "InvoiceId", "BillingRecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessPartyId",
                table: "Invoices",
                column: "BusinessPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ManagerId",
                table: "Invoices",
                column: "ManagerId");

            migrationBuilder.Sql("""
                CREATE TEMP TABLE "_LegacyInvoiceMap" ("BillingRecordId" integer PRIMARY KEY, "InvoiceNumber" varchar(100) NOT NULL) ON COMMIT DROP;
                WITH legacy AS (
                    SELECT b."Id", b."EngagementId", b."Amount", b."CustomerName", b."BillingDate", b."InvoiceNumber",
                           CASE WHEN NULLIF(trim(b."InvoiceNumber"), '') IS NULL OR length(trim(b."InvoiceNumber")) > 100
                                THEN 'LEGACY-B-' || b."Id"::text
                                ELSE trim(b."InvoiceNumber") || CASE WHEN count(*) OVER (PARTITION BY trim(b."InvoiceNumber")) > 1 THEN '-' || b."Id"::text ELSE '' END END AS "MigratedInvoiceNumber"
                    FROM "BillingRecords" b
                    WHERE b."InvoiceNumber" IS NOT NULL OR b."BillingDate" IS NOT NULL
                )
                INSERT INTO "_LegacyInvoiceMap" ("BillingRecordId", "InvoiceNumber") SELECT "Id", "MigratedInvoiceNumber" FROM legacy;
                WITH legacy AS (
                    SELECT b."Id", b."EngagementId", b."Amount", b."CustomerName", b."BillingDate", m."InvoiceNumber"
                    FROM "BillingRecords" b JOIN "_LegacyInvoiceMap" m ON m."BillingRecordId" = b."Id"
                )
                INSERT INTO "Invoices" ("InvoiceNumber", "InvoiceDate", "Flow", "Status", "Total", "IssuerName", "RecipientName", "BusinessPartyId", "ManagerId", "CancellationReason", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "Version")
                SELECT l."InvoiceNumber", COALESCE(l."BillingDate", CURRENT_DATE), 0,
                       CASE b."Status" WHEN 7 THEN 3 WHEN 6 THEN 2 WHEN 5 THEN 1 ELSE 0 END,
                       l."Amount", bp."Name", l."CustomerName", e."BusinessPartyId", NULL,
                       CASE WHEN b."Status" = 7 THEN b."CancellationReason" ELSE NULL END,
                       b."CreatedAt", b."CreatedBy", b."UpdatedAt", b."UpdatedBy", 1
                FROM legacy l JOIN "BillingRecords" b ON b."Id" = l."Id"
                JOIN "Engagements" e ON e."Id" = l."EngagementId"
                JOIN "BusinessParties" bp ON bp."Id" = e."BusinessPartyId";
                INSERT INTO "InvoiceLines" ("InvoiceId", "BillingRecordId", "AllocatedAmount", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "Version")
                SELECT i."Id", m."BillingRecordId", b."Amount", b."CreatedAt", b."CreatedBy", b."UpdatedAt", b."UpdatedBy", 1
                FROM "_LegacyInvoiceMap" m JOIN "BillingRecords" b ON b."Id" = m."BillingRecordId" JOIN "Invoices" i ON i."InvoiceNumber" = m."InvoiceNumber";
                INSERT INTO "CustomerReceiptAllocations" ("CustomerReceiptId", "InvoiceId", "Amount", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "Version")
                SELECT r."Id", i."Id", r."Amount", r."CreatedAt", r."CreatedBy", r."UpdatedAt", r."UpdatedBy", 1
                FROM "CustomerReceipts" r JOIN "_LegacyInvoiceMap" m ON m."BillingRecordId" = r."BillingRecordId" JOIN "Invoices" i ON i."InvoiceNumber" = m."InvoiceNumber";
                WITH paid AS (
                    SELECT a."InvoiceId", SUM(a."Amount") AS "Amount"
                    FROM "CustomerReceiptAllocations" a
                    JOIN "CustomerReceipts" r ON r."Id" = a."CustomerReceiptId"
                    WHERE NOT r."IsCancelled"
                    GROUP BY a."InvoiceId"
                )
                UPDATE "Invoices" i
                SET "Status" = CASE WHEN paid."Amount" >= i."Total" THEN 2 WHEN paid."Amount" > 0 THEN 1 ELSE i."Status" END
                FROM paid
                WHERE paid."InvoiceId" = i."Id" AND i."Status" <> 3;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerReceipts_BillingRecords_BillingRecordId",
                table: "CustomerReceipts");

            migrationBuilder.DropIndex(
                name: "IX_CustomerReceipts_BillingRecordId",
                table: "CustomerReceipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Billing_Invoice",
                table: "BillingRecords");

            migrationBuilder.DropColumn(
                name: "BillingRecordId",
                table: "CustomerReceipts");

            migrationBuilder.DropColumn(
                name: "BillingDate",
                table: "BillingRecords");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "BillingRecords");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "InvoiceLines" GROUP BY "BillingRecordId" HAVING count(*) > 1)
                       OR EXISTS (SELECT 1 FROM "InvoiceLines" GROUP BY "InvoiceId" HAVING count(*) > 1)
                       OR EXISTS (SELECT 1 FROM "CustomerReceiptAllocations" GROUP BY "CustomerReceiptId" HAVING count(*) > 1)
                       OR EXISTS (SELECT 1 FROM "CustomerReceipts" r WHERE NOT EXISTS (SELECT 1 FROM "CustomerReceiptAllocations" a WHERE a."CustomerReceiptId" = r."Id")) THEN
                        RAISE EXCEPTION 'InvoiceDocuments cannot be downgraded after split, consolidated or unallocated documents exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddColumn<int>(
                name: "BillingRecordId",
                table: "CustomerReceipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BillingDate",
                table: "BillingRecords",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "BillingRecords",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "BillingRecords" b
                SET "BillingDate" = i."InvoiceDate", "InvoiceNumber" = i."InvoiceNumber"
                FROM "InvoiceLines" l JOIN "Invoices" i ON i."Id" = l."InvoiceId"
                WHERE l."BillingRecordId" = b."Id";
                UPDATE "CustomerReceipts" r
                SET "BillingRecordId" = l."BillingRecordId"
                FROM "CustomerReceiptAllocations" a
                JOIN "InvoiceLines" l ON l."InvoiceId" = a."InvoiceId"
                WHERE a."CustomerReceiptId" = r."Id";
                """);

            migrationBuilder.DropTable(
                name: "CustomerReceiptAllocations");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_BillingRecordId",
                table: "CustomerReceipts",
                column: "BillingRecordId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Billing_Invoice",
                table: "BillingRecords",
                sql: "\"Status\" NOT IN (4,5,6) OR (\"BillingDate\" IS NOT NULL AND length(trim(\"InvoiceNumber\")) > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerReceipts_BillingRecords_BillingRecordId",
                table: "CustomerReceipts",
                column: "BillingRecordId",
                principalTable: "BillingRecords",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
