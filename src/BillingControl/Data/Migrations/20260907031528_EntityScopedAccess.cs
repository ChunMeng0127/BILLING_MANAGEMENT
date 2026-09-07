using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntityScopedAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AspNetRoles\" SET \"Name\" = 'InternalUser', \"NormalizedName\" = 'INTERNALUSER' WHERE \"Name\" = 'User';");

            migrationBuilder.AddColumn<int>(
                name: "BusinessPartyId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagerId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkerId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_BusinessPartyId",
                table: "AspNetUsers",
                column: "BusinessPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ManagerId",
                table: "AspNetUsers",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WorkerId",
                table: "AspNetUsers",
                column: "WorkerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_User_AtMostOneLink",
                table: "AspNetUsers",
                sql: "(CASE WHEN \"BusinessPartyId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"ManagerId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"WorkerId\" IS NULL THEN 0 ELSE 1 END) <= 1");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_BusinessParties_BusinessPartyId",
                table: "AspNetUsers",
                column: "BusinessPartyId",
                principalTable: "BusinessParties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Managers_ManagerId",
                table: "AspNetUsers",
                column: "ManagerId",
                principalTable: "Managers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Workers_WorkerId",
                table: "AspNetUsers",
                column: "WorkerId",
                principalTable: "Workers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AspNetRoles\" SET \"Name\" = 'User', \"NormalizedName\" = 'USER' WHERE \"Name\" = 'InternalUser';");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_BusinessParties_BusinessPartyId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Managers_ManagerId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Workers_WorkerId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_BusinessPartyId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ManagerId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_WorkerId",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_User_AtMostOneLink",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BusinessPartyId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "AspNetUsers");
        }
    }
}
