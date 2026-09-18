using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10AOutboundPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "DocumentRequestBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DocumentRequestBatchStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestBatchId = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Actor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestBatchStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequestBatchStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.CheckConstraint("CK_DocumentRequestBatchStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5, 6)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "FK_DocumentRequestBatchStatusHistories_DocumentRequestBatches_~",
                        column: x => x.DocumentRequestBatchId,
                        principalTable: "DocumentRequestBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundBatchSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestBatchId = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    WhatsAppConversationId = table.Column<int>(type: "integer", nullable: false),
                    ConversationAuthorizationVersion = table.Column<int>(type: "integer", nullable: false),
                    ParticipantSetHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QueuedByActor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundBatchSnapshots", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundBatchSnapshot_AuthorizationVersion", "\"ConversationAuthorizationVersion\" > 0");
                    table.CheckConstraint("CK_WhatsAppOutboundBatchSnapshot_ParticipantSetHash", "\"ParticipantSetHash\" ~ '^[0-9A-Fa-f]{64}$'");
                    table.CheckConstraint("CK_WhatsAppOutboundBatchSnapshot_Text", "length(btrim(\"QueuedByActor\")) > 0 AND length(btrim(\"CorrelationId\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundBatchSnapshots_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundBatchSnapshots_DocumentRequestBatches_Docum~",
                        column: x => x.DocumentRequestBatchId,
                        principalTable: "DocumentRequestBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundBatchSnapshots_WhatsAppConversations_WhatsA~",
                        column: x => x.WhatsAppConversationId,
                        principalTable: "WhatsAppConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundEngagementScopeSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppOutboundBatchSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    WhatsAppConversationEngagementScopeId = table.Column<int>(type: "integer", nullable: false),
                    EngagementId = table.Column<int>(type: "integer", nullable: false),
                    ApprovedAuthorizationVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundEngagementScopeSnapshots", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundEngagementScopeSnapshot_ApprovedVersion", "\"ApprovedAuthorizationVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundEngagementScopeSnapshots_Engagements_Engage~",
                        column: x => x.EngagementId,
                        principalTable: "Engagements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundEngagementScopeSnapshots_WhatsAppConversati~",
                        column: x => x.WhatsAppConversationEngagementScopeId,
                        principalTable: "WhatsAppConversationEngagementScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundEngagementScopeSnapshots_WhatsAppOutboundBa~",
                        column: x => x.WhatsAppOutboundBatchSnapshotId,
                        principalTable: "WhatsAppOutboundBatchSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DocumentRequestItems_DocumentRequestId_Id",
                table: "DocumentRequestItems",
                columns: new[] { "DocumentRequestId", "Id" });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundItemSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppOutboundBatchSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequestId = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequestItemId = table.Column<int>(type: "integer", nullable: false),
                    RequestRevision = table.Column<int>(type: "integer", nullable: false),
                    RequirementNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ItemStatusSnapshot = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundItemSnapshots", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundItemSnapshot_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_WhatsAppOutboundItemSnapshot_Revision", "\"RequestRevision\" > 0");
                    table.CheckConstraint("CK_WhatsAppOutboundItemSnapshot_Status", "\"ItemStatusSnapshot\" IN (0, 1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_WhatsAppOutboundItemSnapshot_Text", "length(btrim(\"RequirementNameSnapshot\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundItemSnapshots_DocumentRequestItems_Document~",
                        columns: x => new { x.DocumentRequestId, x.DocumentRequestItemId },
                        principalTable: "DocumentRequestItems",
                        principalColumns: new[] { "DocumentRequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundItemSnapshots_DocumentRequests_DocumentRequ~",
                        column: x => x.DocumentRequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundItemSnapshots_WhatsAppOutboundBatchSnapshot~",
                        column: x => x.WhatsAppOutboundBatchSnapshotId,
                        principalTable: "WhatsAppOutboundBatchSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestBatchId = table.Column<int>(type: "integer", nullable: false),
                    WhatsAppOutboundBatchSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    LogicalMessageKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    BusinessEndpointKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    ProviderAccountReference = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    DestinationKind = table.Column<int>(type: "integer", nullable: false),
                    ProviderDestinationKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    NormalizedE164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    ProviderRecipientKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ContentKind = table.Column<int>(type: "integer", nullable: false),
                    TextBody = table.Column<string>(type: "text", nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TemplateLanguage = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    TemplateParametersSnapshot = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    LastErrorCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ProviderRetryAfterUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundMessages", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_AttemptCount", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_ContentKind", "\"ContentKind\" IN (0, 1)");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_ContentShape", "(\"ContentKind\" = 0 AND \"TextBody\" IS NOT NULL AND length(btrim(\"TextBody\")) > 0 AND \"TemplateName\" IS NULL AND \"TemplateLanguage\" IS NULL AND \"TemplateParametersSnapshot\" IS NULL) OR (\"ContentKind\" = 1 AND \"TextBody\" IS NULL AND \"TemplateName\" IS NOT NULL AND length(btrim(\"TemplateName\")) > 0 AND \"TemplateLanguage\" IS NOT NULL AND length(btrim(\"TemplateLanguage\")) > 0)");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_DestinationKind", "\"DestinationKind\" IN (0, 1)");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_DirectRecipientFacts", "\"DestinationKind\" <> 1 OR (\"NormalizedE164\" IS NULL AND \"ProviderRecipientKey\" IS NULL)");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_NormalizedE164", "\"NormalizedE164\" IS NULL OR \"NormalizedE164\" ~ '^\\+[1-9][0-9]{0,14}$'");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_References", "length(btrim(\"LogicalMessageKey\")) > 0 AND length(btrim(\"CorrelationId\")) > 0 AND length(btrim(\"ProviderName\")) > 0 AND length(btrim(\"BusinessEndpointKey\")) > 0 AND length(btrim(\"ProviderDestinationKey\")) > 0 AND (\"ProviderAccountReference\" IS NULL OR length(btrim(\"ProviderAccountReference\")) > 0)");
                    table.CheckConstraint("CK_WhatsAppOutboundMessage_State", "\"State\" IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundMessages_DocumentRequestBatches_DocumentReq~",
                        column: x => x.DocumentRequestBatchId,
                        principalTable: "DocumentRequestBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundMessages_WhatsAppOutboundBatchSnapshots_Wha~",
                        column: x => x.WhatsAppOutboundBatchSnapshotId,
                        principalTable: "WhatsAppOutboundBatchSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundParticipantSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppOutboundBatchSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    WhatsAppConversationParticipantId = table.Column<int>(type: "integer", nullable: false),
                    ParticipantKind = table.Column<int>(type: "integer", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: true),
                    ContactWhatsAppAddressId = table.Column<int>(type: "integer", nullable: true),
                    BusinessPartyId = table.Column<int>(type: "integer", nullable: true),
                    ManagerId = table.Column<int>(type: "integer", nullable: true),
                    AppUserId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ProviderParticipantKey = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NormalizedE164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DisplayNameSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundParticipantSnapshots", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundParticipantSnapshot_DisplayName", "\"DisplayNameSnapshot\" IS NULL OR length(btrim(\"DisplayNameSnapshot\")) > 0");
                    table.CheckConstraint("CK_WhatsAppOutboundParticipantSnapshot_Identity", "(\"ParticipantKind\" = 0 AND \"ContactId\" IS NOT NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 1 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NOT NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 2 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NOT NULL AND \"AppUserId\" IS NULL) OR (\"ParticipantKind\" = 3 AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NOT NULL) OR ((\"ParticipantKind\" = 4 OR \"ParticipantKind\" = 5) AND \"ContactId\" IS NULL AND \"ContactWhatsAppAddressId\" IS NULL AND \"BusinessPartyId\" IS NULL AND \"ManagerId\" IS NULL AND \"AppUserId\" IS NULL)");
                    table.CheckConstraint("CK_WhatsAppOutboundParticipantSnapshot_Kind", "\"ParticipantKind\" IN (0, 1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_WhatsAppOutboundParticipantSnapshot_NormalizedE164", "\"NormalizedE164\" IS NULL OR \"NormalizedE164\" ~ '^\\+[1-9][0-9]{0,14}$'");
                    table.CheckConstraint("CK_WhatsAppOutboundParticipantSnapshot_References", "\"ProviderParticipantKey\" IS NULL OR length(btrim(\"ProviderParticipantKey\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_AspNetUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_BusinessParties_Busine~",
                        column: x => x.BusinessPartyId,
                        principalTable: "BusinessParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_ContactWhatsAppAddress~",
                        columns: x => new { x.ContactId, x.ContactWhatsAppAddressId },
                        principalTable: "ContactWhatsAppAddresses",
                        principalColumns: new[] { "ContactId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_Managers_ManagerId",
                        column: x => x.ManagerId,
                        principalTable: "Managers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_WhatsAppConversationPa~",
                        column: x => x.WhatsAppConversationParticipantId,
                        principalTable: "WhatsAppConversationParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundParticipantSnapshots_WhatsAppOutboundBatchS~",
                        column: x => x.WhatsAppOutboundBatchSnapshotId,
                        principalTable: "WhatsAppOutboundBatchSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundRequestSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppOutboundBatchSnapshotId = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequestId = table.Column<int>(type: "integer", nullable: false),
                    RequestRevision = table.Column<int>(type: "integer", nullable: false),
                    RequestVersion = table.Column<long>(type: "bigint", nullable: false),
                    EngagementId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    CustomerNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    ServiceNameSnapshot = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundRequestSnapshots", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundRequestSnapshot_Revision", "\"RequestRevision\" > 0");
                    table.CheckConstraint("CK_WhatsAppOutboundRequestSnapshot_Text", "length(btrim(\"CustomerNameSnapshot\")) > 0 AND length(btrim(\"ServiceNameSnapshot\")) > 0");
                    table.CheckConstraint("CK_WhatsAppOutboundRequestSnapshot_Version", "\"RequestVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundRequestSnapshots_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundRequestSnapshots_DocumentRequests_DocumentR~",
                        column: x => x.DocumentRequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundRequestSnapshots_Engagements_EngagementId",
                        column: x => x.EngagementId,
                        principalTable: "Engagements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundRequestSnapshots_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundRequestSnapshots_WhatsAppOutboundBatchSnaps~",
                        column: x => x.WhatsAppOutboundBatchSnapshotId,
                        principalTable: "WhatsAppOutboundBatchSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppOutboundMessageAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WhatsAppOutboundMessageId = table.Column<int>(type: "integer", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Disposition = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ErrorCategory = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    RetryAfterUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Actor = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppOutboundMessageAttempts", x => x.Id);
                    table.CheckConstraint("CK_WhatsAppOutboundMessageAttempt_Dates", "\"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"StartedAt\"");
                    table.CheckConstraint("CK_WhatsAppOutboundMessageAttempt_Number", "\"AttemptNumber\" > 0");
                    table.CheckConstraint("CK_WhatsAppOutboundMessageAttempt_Text", "length(btrim(\"CorrelationId\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_WhatsAppOutboundMessageAttempts_WhatsAppOutboundMessages_Wh~",
                        column: x => x.WhatsAppOutboundMessageId,
                        principalTable: "WhatsAppOutboundMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestBatches_Status",
                table: "DocumentRequestBatches",
                column: "Status");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentRequestBatch_Status",
                table: "DocumentRequestBatches",
                sql: "\"Status\" IN (0, 1, 2, 3, 4, 5, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestBatchStatusHistories_DocumentRequestBatchId_~",
                table: "DocumentRequestBatchStatusHistories",
                columns: new[] { "DocumentRequestBatchId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundBatchSnapshots_ContactId",
                table: "WhatsAppOutboundBatchSnapshots",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundBatchSnapshots_DocumentRequestBatchId",
                table: "WhatsAppOutboundBatchSnapshots",
                column: "DocumentRequestBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundBatchSnapshots_WhatsAppConversationId_Queue~",
                table: "WhatsAppOutboundBatchSnapshots",
                columns: new[] { "WhatsAppConversationId", "QueuedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundEngagementScopeSnapshots_EngagementId",
                table: "WhatsAppOutboundEngagementScopeSnapshots",
                column: "EngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundEngagementScopeSnapshots_WhatsAppConversati~",
                table: "WhatsAppOutboundEngagementScopeSnapshots",
                column: "WhatsAppConversationEngagementScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundEngagementScopeSnapshots_WhatsAppOutboundB~1",
                table: "WhatsAppOutboundEngagementScopeSnapshots",
                columns: new[] { "WhatsAppOutboundBatchSnapshotId", "WhatsAppConversationEngagementScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundEngagementScopeSnapshots_WhatsAppOutboundBa~",
                table: "WhatsAppOutboundEngagementScopeSnapshots",
                columns: new[] { "WhatsAppOutboundBatchSnapshotId", "EngagementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundItemSnapshots_DocumentRequestId_DocumentReq~",
                table: "WhatsAppOutboundItemSnapshots",
                columns: new[] { "DocumentRequestId", "DocumentRequestItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundItemSnapshots_DocumentRequestItemId",
                table: "WhatsAppOutboundItemSnapshots",
                column: "DocumentRequestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundItemSnapshots_WhatsAppOutboundBatchSnapshot~",
                table: "WhatsAppOutboundItemSnapshots",
                columns: new[] { "WhatsAppOutboundBatchSnapshotId", "DocumentRequestItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessageAttempts_WhatsAppOutboundMessageId_~1",
                table: "WhatsAppOutboundMessageAttempts",
                columns: new[] { "WhatsAppOutboundMessageId", "AttemptNumber", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessageAttempts_WhatsAppOutboundMessageId_A~",
                table: "WhatsAppOutboundMessageAttempts",
                columns: new[] { "WhatsAppOutboundMessageId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessages_DocumentRequestBatchId",
                table: "WhatsAppOutboundMessages",
                column: "DocumentRequestBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessages_LogicalMessageKey",
                table: "WhatsAppOutboundMessages",
                column: "LogicalMessageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessages_ProviderMessageId",
                table: "WhatsAppOutboundMessages",
                column: "ProviderMessageId",
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessages_State",
                table: "WhatsAppOutboundMessages",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundMessages_WhatsAppOutboundBatchSnapshotId",
                table: "WhatsAppOutboundMessages",
                column: "WhatsAppOutboundBatchSnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_AppUserId",
                table: "WhatsAppOutboundParticipantSnapshots",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_BusinessPartyId",
                table: "WhatsAppOutboundParticipantSnapshots",
                column: "BusinessPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_ContactId_ContactWhats~",
                table: "WhatsAppOutboundParticipantSnapshots",
                columns: new[] { "ContactId", "ContactWhatsAppAddressId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_ManagerId",
                table: "WhatsAppOutboundParticipantSnapshots",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_WhatsAppConversationPa~",
                table: "WhatsAppOutboundParticipantSnapshots",
                column: "WhatsAppConversationParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_WhatsAppOutboundBatch~1",
                table: "WhatsAppOutboundParticipantSnapshots",
                columns: new[] { "WhatsAppOutboundBatchSnapshotId", "WhatsAppConversationParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundParticipantSnapshots_WhatsAppOutboundBatchS~",
                table: "WhatsAppOutboundParticipantSnapshots",
                columns: new[] { "WhatsAppOutboundBatchSnapshotId", "ParticipantKind" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundRequestSnapshots_CustomerId",
                table: "WhatsAppOutboundRequestSnapshots",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundRequestSnapshots_DocumentRequestId",
                table: "WhatsAppOutboundRequestSnapshots",
                column: "DocumentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundRequestSnapshots_EngagementId",
                table: "WhatsAppOutboundRequestSnapshots",
                column: "EngagementId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundRequestSnapshots_ServiceId",
                table: "WhatsAppOutboundRequestSnapshots",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppOutboundRequestSnapshots_WhatsAppOutboundBatchSnaps~",
                table: "WhatsAppOutboundRequestSnapshots",
                columns: new[] { "WhatsAppOutboundBatchSnapshotId", "DocumentRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentRequestBatchStatusHistories");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundEngagementScopeSnapshots");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundItemSnapshots");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundMessageAttempts");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundParticipantSnapshots");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundRequestSnapshots");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundMessages");

            migrationBuilder.DropTable(
                name: "WhatsAppOutboundBatchSnapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DocumentRequestItems_DocumentRequestId_Id",
                table: "DocumentRequestItems");

            migrationBuilder.DropIndex(
                name: "IX_DocumentRequestBatches_Status",
                table: "DocumentRequestBatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentRequestBatch_Status",
                table: "DocumentRequestBatches");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DocumentRequestBatches");
        }
    }
}
