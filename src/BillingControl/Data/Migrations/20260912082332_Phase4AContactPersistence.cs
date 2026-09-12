using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4AContactPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.CheckConstraint("CK_Contact_Text", "length(btrim(\"Name\")) > 0 AND (\"PreferredLanguage\" IS NULL OR length(btrim(\"PreferredLanguage\")) > 0)");
                });

            migrationBuilder.CreateTable(
                name: "ContactCustomerLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Role = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactCustomerLinks", x => x.Id);
                    table.CheckConstraint("CK_ContactCustomerLink_EffectiveDates", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                    table.CheckConstraint("CK_ContactCustomerLink_EffectiveState", "(\"IsActive\" AND \"EffectiveTo\" IS NULL) OR (NOT \"IsActive\" AND \"EffectiveTo\" IS NOT NULL)");
                    table.CheckConstraint("CK_ContactCustomerLink_Role", "\"Role\" IS NULL OR length(btrim(\"Role\")) > 0");
                    table.ForeignKey(
                        name: "FK_ContactCustomerLinks_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContactCustomerLinks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContactStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Actor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_ContactStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_ContactStatusHistories_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContactWhatsAppAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    NormalizedE164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProviderWaId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConsentState = table.Column<int>(type: "integer", nullable: false),
                    ConsentRecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentSource = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ConsentEvidenceReference = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    LastOptOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastOptOutReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactWhatsAppAddresses", x => x.Id);
                    table.CheckConstraint("CK_ContactWhatsAppAddress_ConsentState", "\"ConsentState\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_ContactWhatsAppAddress_DoNotWhatsAppConsent", "\"ConsentState\" <> 2 OR (\"ConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"ConsentSource\", ''))) > 0 AND \"LastOptOutAt\" IS NOT NULL AND length(btrim(COALESCE(\"LastOptOutReason\", ''))) > 0)");
                    table.CheckConstraint("CK_ContactWhatsAppAddress_NormalizedE164", "\"NormalizedE164\" ~ '^\\+[1-9][0-9]{0,14}$'");
                    table.CheckConstraint("CK_ContactWhatsAppAddress_OptedInConsent", "\"ConsentState\" <> 1 OR (\"ConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"ConsentSource\", ''))) > 0 AND length(btrim(COALESCE(\"ConsentEvidenceReference\", ''))) > 0)");
                    table.CheckConstraint("CK_ContactWhatsAppAddress_PrimaryActive", "NOT \"IsPrimary\" OR \"IsActive\"");
                    table.CheckConstraint("CK_ContactWhatsAppAddress_UnknownConsent", "\"ConsentState\" <> 0 OR (\"ConsentRecordedAt\" IS NULL AND \"ConsentSource\" IS NULL AND \"ConsentEvidenceReference\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ContactWhatsAppAddresses_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContactCustomerLinkHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactCustomerLinkId = table.Column<int>(type: "integer", nullable: false),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    NewEffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PreviousEffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    NewEffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    PreviousRole = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    NewRole = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PreviousNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Actor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactCustomerLinkHistories", x => x.Id);
                    table.CheckConstraint("CK_ContactCustomerLinkHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_ContactCustomerLinkHistories_ContactCustomerLinks_ContactCu~",
                        column: x => x.ContactCustomerLinkId,
                        principalTable: "ContactCustomerLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContactWhatsAppAddressHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactWhatsAppAddressId = table.Column<int>(type: "integer", nullable: false),
                    PreviousConsentState = table.Column<int>(type: "integer", nullable: true),
                    NewConsentState = table.Column<int>(type: "integer", nullable: false),
                    PreviousConsentRecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewConsentRecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousConsentSource = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NewConsentSource = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    PreviousConsentEvidenceReference = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NewConsentEvidenceReference = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    PreviousLastOptOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewLastOptOutAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousLastOptOutReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewLastOptOutReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousIsPrimary = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousProviderWaId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NewProviderWaId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Actor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactWhatsAppAddressHistories", x => x.Id);
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_ConsentState", "(\"PreviousConsentState\" IS NULL OR \"PreviousConsentState\" IN (0, 1, 2)) AND \"NewConsentState\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_NewDoNotWhatsAppConsent", "\"NewConsentState\" <> 2 OR (\"NewConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewConsentSource\", ''))) > 0 AND \"NewLastOptOutAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewLastOptOutReason\", ''))) > 0)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_NewOptedInConsent", "\"NewConsentState\" <> 1 OR (\"NewConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewConsentSource\", ''))) > 0 AND length(btrim(COALESCE(\"NewConsentEvidenceReference\", ''))) > 0)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_NewUnknownConsent", "\"NewConsentState\" <> 0 OR (\"NewConsentRecordedAt\" IS NULL AND \"NewConsentSource\" IS NULL AND \"NewConsentEvidenceReference\" IS NULL)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousCreationSnapshot", "\"PreviousConsentState\" IS NOT NULL OR (\"PreviousConsentRecordedAt\" IS NULL AND \"PreviousConsentSource\" IS NULL AND \"PreviousConsentEvidenceReference\" IS NULL AND \"PreviousLastOptOutAt\" IS NULL AND \"PreviousLastOptOutReason\" IS NULL)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousDoNotWhatsAppConsent", "\"PreviousConsentState\" <> 2 OR (\"PreviousConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousConsentSource\", ''))) > 0 AND \"PreviousLastOptOutAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousLastOptOutReason\", ''))) > 0)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousOptedInConsent", "\"PreviousConsentState\" <> 1 OR (\"PreviousConsentRecordedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousConsentSource\", ''))) > 0 AND length(btrim(COALESCE(\"PreviousConsentEvidenceReference\", ''))) > 0)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_PreviousUnknownConsent", "\"PreviousConsentState\" <> 0 OR (\"PreviousConsentRecordedAt\" IS NULL AND \"PreviousConsentSource\" IS NULL AND \"PreviousConsentEvidenceReference\" IS NULL)");
                    table.CheckConstraint("CK_ContactWhatsAppAddressHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_ContactWhatsAppAddressHistories_ContactWhatsAppAddresses_Co~",
                        column: x => x.ContactWhatsAppAddressId,
                        principalTable: "ContactWhatsAppAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactCustomerLinkHistories_ContactCustomerLinkId_Occurred~",
                table: "ContactCustomerLinkHistories",
                columns: new[] { "ContactCustomerLinkId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactCustomerLinks_ContactId",
                table: "ContactCustomerLinks",
                column: "ContactId",
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_ContactCustomerLinks_ContactId_CustomerId",
                table: "ContactCustomerLinks",
                columns: new[] { "ContactId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactCustomerLinks_CustomerId",
                table: "ContactCustomerLinks",
                column: "CustomerId",
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Name",
                table: "Contacts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ContactStatusHistories_ContactId_OccurredAt_Id",
                table: "ContactStatusHistories",
                columns: new[] { "ContactId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactWhatsAppAddresses_ContactId",
                table: "ContactWhatsAppAddresses",
                column: "ContactId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"IsPrimary\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_ContactWhatsAppAddresses_ContactId_NormalizedE164",
                table: "ContactWhatsAppAddresses",
                columns: new[] { "ContactId", "NormalizedE164" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactWhatsAppAddresses_NormalizedE164",
                table: "ContactWhatsAppAddresses",
                column: "NormalizedE164",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_ContactWhatsAppAddresses_ProviderWaId",
                table: "ContactWhatsAppAddresses",
                column: "ProviderWaId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactWhatsAppAddressHistories_ContactWhatsAppAddressId_Oc~",
                table: "ContactWhatsAppAddressHistories",
                columns: new[] { "ContactWhatsAppAddressId", "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactCustomerLinkHistories");

            migrationBuilder.DropTable(
                name: "ContactStatusHistories");

            migrationBuilder.DropTable(
                name: "ContactWhatsAppAddressHistories");

            migrationBuilder.DropTable(
                name: "ContactCustomerLinks");

            migrationBuilder.DropTable(
                name: "ContactWhatsAppAddresses");

            migrationBuilder.DropTable(
                name: "Contacts");
        }
    }
}
