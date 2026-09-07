using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceIssuerAndReceiptIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_BusinessPartyId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ManagerId",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_Party",
                table: "Invoices");

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "Invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT i."Id"
                        FROM "Invoices" i
                        LEFT JOIN "InvoiceLines" il ON il."InvoiceId" = i."Id"
                        LEFT JOIN "BillingRecords" b ON b."Id" = il."BillingRecordId"
                        LEFT JOIN "Engagements" e ON e."Id" = b."EngagementId"
                        WHERE i."Flow" = 0
                        GROUP BY i."Id"
                        HAVING count(DISTINCT e."CustomerId") <> 1
                    ) THEN
                        RAISE EXCEPTION 'Existing customer invoices must each contain exactly one end customer before this migration can continue.';
                    END IF;
                END $$;

                UPDATE "Invoices" i
                SET "CustomerId" = source."CustomerId"
                FROM (
                    SELECT il."InvoiceId", min(e."CustomerId") AS "CustomerId"
                    FROM "InvoiceLines" il
                    JOIN "BillingRecords" b ON b."Id" = il."BillingRecordId"
                    JOIN "Engagements" e ON e."Id" = b."EngagementId"
                    GROUP BY il."InvoiceId"
                ) source
                WHERE i."Id" = source."InvoiceId" AND i."Flow" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessPartyId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "BusinessPartyId", "InvoiceNumber" },
                unique: true,
                filter: "\"Flow\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices",
                column: "InvoiceNumber",
                unique: true,
                filter: "\"Flow\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ManagerId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "ManagerId", "InvoiceNumber" },
                unique: true,
                filter: "\"Flow\" = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_Party",
                table: "Invoices",
                sql: "(\"Flow\" = 0 AND \"BusinessPartyId\" IS NOT NULL AND \"CustomerId\" IS NOT NULL AND \"ManagerId\" IS NULL) OR (\"Flow\" = 1 AND \"BusinessPartyId\" IS NOT NULL AND \"CustomerId\" IS NULL AND \"ManagerId\" IS NOT NULL) OR (\"Flow\" = 2 AND \"BusinessPartyId\" IS NULL AND \"CustomerId\" IS NULL AND \"ManagerId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Customers_CustomerId",
                table: "Invoices",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Customers_CustomerId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_BusinessPartyId_InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ManagerId_InvoiceNumber",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_Party",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "Invoices");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_Party",
                table: "Invoices",
                sql: "(\"Flow\" = 0 AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NULL) OR (\"Flow\" = 1 AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NOT NULL) OR (\"Flow\" = 2 AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NOT NULL)");
        }
    }
}
