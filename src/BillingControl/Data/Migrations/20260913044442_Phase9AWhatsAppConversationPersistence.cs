using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9AWhatsAppConversationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BusinessEndpointKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ProviderAccountReference = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ProviderConversationKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    DirectContactWhatsAppAddressId = table.Column<int>(type: "integer", nullable: true),
                    AuthorizationVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConversations", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConversation_AuthorizationVersion", "\"AuthorizationVersion\" > 0");
                    table.CheckConstraint("CK_WhatsAppConversation_DirectAddress", "\"Kind\" <> 0 OR \"DirectContactWhatsAppAddressId\" IS NOT NULL");
                    table.CheckConstraint("CK_WhatsAppConversation_GroupNoDirectAddress", "\"Kind\" <> 1 OR \"DirectContactWhatsAppAddressId\" IS NULL");
                    table.CheckConstraint("CK_WhatsAppConversation_Kind", "\"Kind\" IN (0, 1)");
                    table.CheckConstraint("CK_WhatsAppConversation_References", "length(btrim(\"ProviderName\")) > 0 AND length(btrim(\"BusinessEndpointKey\")) > 0 AND length(btrim(\"ProviderConversationKey\")) > 0 AND (\"ProviderAccountReference\" IS NULL OR length(btrim(\"ProviderAccountReference\")) > 0)");
                    table.CheckConstraint("CK_WhatsAppConversation_Status", "\"Status\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_WhatsAppConversations_ContactWhatsAppAddresses_DirectContac~",
                        column: x => x.DirectContactWhatsAppAddressId,
                        principalTable: "ContactWhatsAppAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversationEngagementScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppConversationId = table.Column<int>(type: "integer", nullable: false),
                    EngagementId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedAuthorizationVersion = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ApprovalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConversationEngagementScopes", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScope_ApprovalFacts", "length(btrim(\"ApprovedByActor\")) > 0 AND (\"ApprovalReason\" IS NULL OR length(btrim(\"ApprovalReason\")) > 0)");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScope_ApprovedVersion", "\"ApprovedAuthorizationVersion\" > 0");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScope_RevocationFacts", "(\"IsActive\" AND \"RevokedAt\" IS NULL AND \"RevokedByActor\" IS NULL AND \"RevocationReason\" IS NULL) OR (NOT \"IsActive\" AND \"RevokedAt\" IS NOT NULL AND \"RevokedByActor\" IS NOT NULL AND length(btrim(\"RevokedByActor\")) > 0 AND (\"RevocationReason\" IS NULL OR length(btrim(\"RevocationReason\")) > 0))");
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationEngagementScopes_Engagements_Engagement~",
                        column: x => x.EngagementId,
                        principalTable: "Engagements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationEngagementScopes_WhatsAppConversations_~",
                        column: x => x.WhatsAppConversationId,
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversationHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppConversationId = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    PreviousAuthorizationVersion = table.Column<int>(type: "integer", nullable: true),
                    NewAuthorizationVersion = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_WhatsAppConversationHistories", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConversationHistory_Status", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2)) AND \"NewStatus\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_WhatsAppConversationHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.CheckConstraint("CK_WhatsAppConversationHistory_Versions", "(\"PreviousAuthorizationVersion\" IS NULL OR \"PreviousAuthorizationVersion\" > 0) AND \"NewAuthorizationVersion\" > 0 AND ((\"PreviousStatus\" IS NULL AND \"PreviousAuthorizationVersion\" IS NULL) OR (\"PreviousStatus\" IS NOT NULL AND \"PreviousAuthorizationVersion\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationHistories_WhatsAppConversations_WhatsAp~",
                        column: x => x.WhatsAppConversationId,
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversationParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppConversationId = table.Column<int>(type: "integer", nullable: false),
                    ParticipantKind = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    ContactWhatsAppAddressId = table.Column<int>(type: "integer", nullable: true),
                    BusinessPartyId = table.Column<int>(type: "integer", nullable: true),
                    ManagerId = table.Column<int>(type: "integer", nullable: true),
                    AppUserId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ProviderParticipantKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NormalizedE164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DisplayNameSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConversationParticipants", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_Dates", "\"LeftAt\" IS NULL OR \"LeftAt\" >= \"JoinedAt\"");
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_DisplayName", "\"DisplayNameSnapshot\" IS NULL OR length(btrim(\"DisplayNameSnapshot\")) > 0");
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_Identity", "(\"ParticipantKind\" = 0 AND \"ContactId\" IS NOT NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 1 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 2 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NOT NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 3 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NOT NULL) OR ((\"ParticipantKind\" = 4 OR \"ParticipantKind\" = 5) AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL)");
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_Kind", "\"ParticipantKind\" IN (0, 1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_Lifecycle", "(\"IsActive\" AND \"LeftAt\" IS NULL) OR (NOT \"IsActive\" AND \"LeftAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_NormalizedE164", "\"NormalizedE164\" IS NULL OR \"NormalizedE164\" ~ '^\\+[1-9][0-9]{0,14}$'");
                    table.CheckConstraint("CK_WhatsAppConversationParticipant_References", "\"ProviderParticipantKey\" IS NULL OR length(btrim(\"ProviderParticipantKey\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipants_AspNetUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipants_BusinessParties_BusinessPa~",
                        column: x => x.BusinessPartyId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipants_ContactWhatsAppAddresses_C~",
                        column: x => x.ContactWhatsAppAddressId,
                        principalTable: "ContactWhatsAppAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipants_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipants_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipants_WhatsAppConversations_What~",
                        column: x => x.WhatsAppConversationId,
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversationEngagementScopeHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppConversationEngagementScopeId = table.Column<int>(type: "integer", nullable: false),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousApprovedAuthorizationVersion = table.Column<int>(type: "integer", nullable: true),
                    NewApprovedAuthorizationVersion = table.Column<int>(type: "integer", nullable: false),
                    PreviousApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousApprovedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NewApprovedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PreviousApprovalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewApprovalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PreviousRevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewRevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousRevokedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NewRevokedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    PreviousRevocationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewRevocationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_WhatsAppConversationEngagementScopeHistories", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_ApprovalFacts", "length(btrim(\"NewApprovedByActor\")) > 0 AND (\"NewApprovalReason\" IS NULL OR length(btrim(\"NewApprovalReason\")) > 0) AND (\"NewRevocationReason\" IS NULL OR length(btrim(\"NewRevocationReason\")) > 0)");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_NewLifecycle", "(\"NewIsActive\" AND \"NewRevokedAt\" IS NULL AND \"NewRevokedByActor\" IS NULL AND \"NewRevocationReason\" IS NULL) OR (NOT \"NewIsActive\" AND \"NewRevokedAt\" IS NOT NULL AND length(btrim(COALESCE(\"NewRevokedByActor\", ''))) > 0)");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_NewVersion", "\"NewApprovedAuthorizationVersion\" > 0");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_PreviousLifecycle", "(\"PreviousIsActive\" IS NULL AND \"PreviousApprovedAuthorizationVersion\" IS NULL AND \"PreviousApprovedAt\" IS NULL AND \"PreviousApprovedByActor\" IS NULL AND \"PreviousApprovalReason\" IS NULL AND \"PreviousRevokedAt\" IS NULL AND \"PreviousRevokedByActor\" IS NULL AND \"PreviousRevocationReason\" IS NULL) OR (\"PreviousIsActive\" AND \"PreviousApprovedAuthorizationVersion\" IS NOT NULL AND \"PreviousApprovedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousApprovedByActor\", ''))) > 0 AND \"PreviousRevokedAt\" IS NULL AND \"PreviousRevokedByActor\" IS NULL AND \"PreviousRevocationReason\" IS NULL) OR (NOT \"PreviousIsActive\" AND \"PreviousApprovedAuthorizationVersion\" IS NOT NULL AND \"PreviousApprovedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousApprovedByActor\", ''))) > 0 AND \"PreviousRevokedAt\" IS NOT NULL AND length(btrim(COALESCE(\"PreviousRevokedByActor\", ''))) > 0)");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_PreviousVersion", "\"PreviousApprovedAuthorizationVersion\" IS NULL OR \"PreviousApprovedAuthorizationVersion\" > 0");
                    table.CheckConstraint("CK_WhatsAppConversationEngagementScopeHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationEngagementScopeHistories_WhatsAppConver~",
                        column: x => x.WhatsAppConversationEngagementScopeId,
                        principalTable: "WhatsAppConversationEngagementScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppConversationParticipantHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppConversationParticipantId = table.Column<int>(type: "integer", nullable: false),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousJoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewJoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousLeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewLeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_WhatsAppConversationParticipantHistories", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppConversationParticipantHistory_Dates", "\"NewLeftAt\" IS NULL OR \"NewLeftAt\" >= \"NewJoinedAt\"");
                    table.CheckConstraint("CK_WhatsAppConversationParticipantHistory_NewLifecycle", "(\"NewIsActive\" AND \"NewLeftAt\" IS NULL) OR (NOT \"NewIsActive\" AND \"NewLeftAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_WhatsAppConversationParticipantHistory_PreviousDates", "\"PreviousLeftAt\" IS NULL OR \"PreviousJoinedAt\" IS NOT NULL AND \"PreviousLeftAt\" >= \"PreviousJoinedAt\"");
                    table.CheckConstraint("CK_WhatsAppConversationParticipantHistory_PreviousLifecycle", "(\"PreviousIsActive\" IS NULL AND \"PreviousJoinedAt\" IS NULL AND \"PreviousLeftAt\" IS NULL) OR (\"PreviousIsActive\" AND \"PreviousJoinedAt\" IS NOT NULL AND \"PreviousLeftAt\" IS NULL) OR (NOT \"PreviousIsActive\" AND \"PreviousJoinedAt\" IS NOT NULL AND \"PreviousLeftAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_WhatsAppConversationParticipantHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppConversationParticipantHistories_WhatsAppConversati~",
                        column: x => x.WhatsAppConversationParticipantId,
                        principalTable: "WhatsAppConversationParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationEngagementScopeHistories_WhatsAppConver~",
                table: "WhatsAppConversationEngagementScopeHistories",
                columns: new[] { "WhatsAppConversationEngagementScopeId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationEngagementScopes_EngagementId",
                table: "WhatsAppConversationEngagementScopes",
                column: "EngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationEngagementScopes_WhatsAppConversationId~",
                table: "WhatsAppConversationEngagementScopes",
                columns: new[] { "WhatsAppConversationId", "EngagementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationHistories_WhatsAppConversationId_Occurr~",
                table: "WhatsAppConversationHistories",
                columns: new[] { "WhatsAppConversationId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipantHistories_WhatsAppConversati~",
                table: "WhatsAppConversationParticipantHistories",
                columns: new[] { "WhatsAppConversationParticipantId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_AppUserId",
                table: "WhatsAppConversationParticipants",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_BusinessPartyId",
                table: "WhatsAppConversationParticipants",
                column: "BusinessPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_ContactId",
                table: "WhatsAppConversationParticipants",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_ContactWhatsAppAddressId",
                table: "WhatsAppConversationParticipants",
                column: "ContactWhatsAppAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_ManagerId",
                table: "WhatsAppConversationParticipants",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_WhatsAppConversationId_App~",
                table: "WhatsAppConversationParticipants",
                columns: new[] { "WhatsAppConversationId", "AppUserId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"AppUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_WhatsAppConversationId_Bus~",
                table: "WhatsAppConversationParticipants",
                columns: new[] { "WhatsAppConversationId", "BusinessPartyId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"BusinessPartyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_WhatsAppConversationId_Con~",
                table: "WhatsAppConversationParticipants",
                columns: new[] { "WhatsAppConversationId", "ContactId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"ContactId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_WhatsAppConversationId_Man~",
                table: "WhatsAppConversationParticipants",
                columns: new[] { "WhatsAppConversationId", "ManagerId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"ManagerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversationParticipants_WhatsAppConversationId_Pro~",
                table: "WhatsAppConversationParticipants",
                columns: new[] { "WhatsAppConversationId", "ProviderParticipantKey" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"ProviderParticipantKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_BusinessEndpointKey_DirectContactWhat~",
                table: "WhatsAppConversations",
                columns: new[] { "BusinessEndpointKey", "DirectContactWhatsAppAddressId" },
                unique: true,
                filter: "\"Kind\" = 0 AND \"Status\" <> 1 AND \"DirectContactWhatsAppAddressId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_DirectContactWhatsAppAddressId",
                table: "WhatsAppConversations",
                column: "DirectContactWhatsAppAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_ProviderName_BusinessEndpointKey_Prov~",
                table: "WhatsAppConversations",
                columns: new[] { "ProviderName", "BusinessEndpointKey", "ProviderConversationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppConversationEngagementScopeHistories");

            migrationBuilder.DropTable(
                name: "WhatsAppConversationHistories");

            migrationBuilder.DropTable(
                name: "WhatsAppConversationParticipantHistories");

            migrationBuilder.DropTable(
                name: "WhatsAppConversationEngagementScopes");

            migrationBuilder.DropTable(
                name: "WhatsAppConversationParticipants");

            migrationBuilder.DropTable(
                name: "WhatsAppConversations");
        }
    }
}
