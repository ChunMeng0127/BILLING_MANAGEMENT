using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BillingControl.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentCollectionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentRequestBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequirementTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<int>(type: "integer", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequirementTemplates", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequirementTemplate_DefaultRequiresActive", "NOT \"IsDefault\" OR \"IsActive\"");
                    table.CheckConstraint("CK_DocumentRequirementTemplate_TemplateVersion", "\"TemplateVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_DocumentRequirementTemplates_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SenderSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    SourceSnapshot = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MimeType = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ByteLength = table.Column<long>(type: "bigint", nullable: true),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SupersedesReceivedDocumentId = table.Column<int>(type: "integer", nullable: true),
                    DuplicateOfReceivedDocumentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedDocuments", x => x.Id);
                    table.CheckConstraint("CK_ReceivedDocument_ByteLength", "\"ByteLength\" IS NULL OR \"ByteLength\" > 0");
                    table.CheckConstraint("CK_ReceivedDocument_DuplicateRequiresCanonical", "\"Status\" <> 4 OR \"DuplicateOfReceivedDocumentId\" IS NOT NULL");
                    table.CheckConstraint("CK_ReceivedDocument_NoSelfDuplicate", "\"DuplicateOfReceivedDocumentId\" IS NULL OR \"DuplicateOfReceivedDocumentId\" <> \"Id\"");
                    table.CheckConstraint("CK_ReceivedDocument_NoSelfSupersession", "\"SupersedesReceivedDocumentId\" IS NULL OR \"SupersedesReceivedDocumentId\" <> \"Id\"");
                    table.CheckConstraint("CK_ReceivedDocument_Sha256Hash", "\"Sha256Hash\" IS NULL OR \"Sha256Hash\" ~ '^[0-9A-Fa-f]{64}$'");
                    table.CheckConstraint("CK_ReceivedDocument_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_ReceivedDocuments_ReceivedDocuments_DuplicateOfReceivedDocu~",
                        column: x => x.DuplicateOfReceivedDocumentId,
                        principalTable: "ReceivedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceivedDocuments_ReceivedDocuments_SupersedesReceivedDocum~",
                        column: x => x.SupersedesReceivedDocumentId,
                        principalTable: "ReceivedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorkItemId = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequirementTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SupersedesRequestId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequests", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequest_NoSelfSupersession", "\"SupersedesRequestId\" IS NULL OR \"SupersedesRequestId\" <> \"Id\"");
                    table.CheckConstraint("CK_DocumentRequest_Revision", "\"Revision\" > 0");
                    table.CheckConstraint("CK_DocumentRequest_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
                    table.ForeignKey(
                        name: "FK_DocumentRequests_DocumentRequests_SupersedesRequestId",
                        column: x => x.SupersedesRequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_DocumentRequirementTemplates_DocumentRequi~",
                        column: x => x.DocumentRequirementTemplateId,
                        principalTable: "DocumentRequirementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequirementTemplateItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequirementTemplateId = table.Column<int>(type: "integer", nullable: false),
                    RequirementKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Wave = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequirementTemplateItems", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequirementTemplateItem_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_DocumentRequirementTemplateItem_Wave", "\"Wave\" IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_DocumentRequirementTemplateItems_DocumentRequirementTemplat~",
                        column: x => x.DocumentRequirementTemplateId,
                        principalTable: "DocumentRequirementTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedDocumentStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceivedDocumentId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ReceivedDocumentStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_ReceivedDocumentStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.CheckConstraint("CK_ReceivedDocumentStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_ReceivedDocumentStatusHistories_ReceivedDocuments_ReceivedD~",
                        column: x => x.ReceivedDocumentId,
                        principalTable: "ReceivedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequestBatchMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestBatchId = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequestId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestBatchMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRequestBatchMembers_DocumentRequestBatches_Document~",
                        column: x => x.DocumentRequestBatchId,
                        principalTable: "DocumentRequestBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequestBatchMembers_DocumentRequests_DocumentReques~",
                        column: x => x.DocumentRequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequestStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_DocumentRequestStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequestStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.CheckConstraint("CK_DocumentRequestStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5, 6, 7)");
                    table.ForeignKey(
                        name: "FK_DocumentRequestStatusHistories_DocumentRequests_DocumentReq~",
                        column: x => x.DocumentRequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequestItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestId = table.Column<int>(type: "integer", nullable: false),
                    DocumentRequirementTemplateItemId = table.Column<int>(type: "integer", nullable: true),
                    RequirementKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequirementName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequirementDescription = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Wave = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestItems", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequestItem_DisplayOrder", "\"DisplayOrder\" >= 0");
                    table.CheckConstraint("CK_DocumentRequestItem_Status", "\"Status\" IN (0, 1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_DocumentRequestItem_Wave", "\"Wave\" IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_DocumentRequestItems_DocumentRequests_DocumentRequestId",
                        column: x => x.DocumentRequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequestItems_DocumentRequirementTemplateItems_Docum~",
                        column: x => x.DocumentRequirementTemplateItemId,
                        principalTable: "DocumentRequirementTemplateItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequestItemEvidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestItemId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedDocumentId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    InactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InactivationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestItemEvidences", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequestItemEvidence_Lifecycle", "(\"IsActive\" AND \"InactivatedAt\" IS NULL AND \"InactivationReason\" IS NULL) OR (NOT \"IsActive\" AND \"InactivatedAt\" IS NOT NULL AND \"InactivationReason\" IS NOT NULL AND length(btrim(\"InactivationReason\")) > 0)");
                    table.ForeignKey(
                        name: "FK_DocumentRequestItemEvidences_DocumentRequestItems_DocumentR~",
                        column: x => x.DocumentRequestItemId,
                        principalTable: "DocumentRequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequestItemEvidences_ReceivedDocuments_ReceivedDocu~",
                        column: x => x.ReceivedDocumentId,
                        principalTable: "ReceivedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequestItemStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestItemId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_DocumentRequestItemStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequestItemStatusHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.CheckConstraint("CK_DocumentRequestItemStatusHistory_Values", "(\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN (0, 1, 2, 3, 4, 5)) AND \"NewStatus\" IN (0, 1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_DocumentRequestItemStatusHistories_DocumentRequestItems_Doc~",
                        column: x => x.DocumentRequestItemId,
                        principalTable: "DocumentRequestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRequestItemEvidenceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocumentRequestItemEvidenceId = table.Column<int>(type: "integer", nullable: false),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_DocumentRequestItemEvidenceHistories", x => x.Id);
                    table.CheckConstraint("CK_DocumentRequestItemEvidenceHistory_Text", "length(btrim(\"Action\")) > 0 AND length(btrim(\"Actor\")) > 0 AND length(btrim(\"Source\")) > 0");
                    table.ForeignKey(
                        name: "FK_DocumentRequestItemEvidenceHistories_DocumentRequestItemEvi~",
                        column: x => x.DocumentRequestItemEvidenceId,
                        principalTable: "DocumentRequestItemEvidences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestBatchMembers_DocumentRequestBatchId_Document~",
                table: "DocumentRequestBatchMembers",
                columns: new[] { "DocumentRequestBatchId", "DocumentRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestBatchMembers_DocumentRequestId",
                table: "DocumentRequestBatchMembers",
                column: "DocumentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestItemEvidenceHistories_DocumentRequestItemEvi~",
                table: "DocumentRequestItemEvidenceHistories",
                columns: new[] { "DocumentRequestItemEvidenceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestItemEvidences_DocumentRequestItemId_Received~",
                table: "DocumentRequestItemEvidences",
                columns: new[] { "DocumentRequestItemId", "ReceivedDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestItemEvidences_ReceivedDocumentId",
                table: "DocumentRequestItemEvidences",
                column: "ReceivedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestItems_DocumentRequestId_RequirementKey",
                table: "DocumentRequestItems",
                columns: new[] { "DocumentRequestId", "RequirementKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestItems_DocumentRequirementTemplateItemId",
                table: "DocumentRequestItems",
                column: "DocumentRequirementTemplateItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestItemStatusHistories_DocumentRequestItemId_Oc~",
                table: "DocumentRequestItemStatusHistories",
                columns: new[] { "DocumentRequestItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_DocumentRequirementTemplateId",
                table: "DocumentRequests",
                column: "DocumentRequirementTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_SupersedesRequestId",
                table: "DocumentRequests",
                column: "SupersedesRequestId",
                unique: true,
                filter: "\"SupersedesRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_WorkItemId",
                table: "DocumentRequests",
                column: "WorkItemId",
                unique: true,
                filter: "\"Status\" IN (0, 1, 2, 3, 4, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_WorkItemId_Revision",
                table: "DocumentRequests",
                columns: new[] { "WorkItemId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestStatusHistories_DocumentRequestId_OccurredAt",
                table: "DocumentRequestStatusHistories",
                columns: new[] { "DocumentRequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequirementTemplateItems_DocumentRequirementTemplat~",
                table: "DocumentRequirementTemplateItems",
                columns: new[] { "DocumentRequirementTemplateId", "RequirementKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequirementTemplates_ServiceId",
                table: "DocumentRequirementTemplates",
                column: "ServiceId",
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequirementTemplates_ServiceId_TemplateKey_Template~",
                table: "DocumentRequirementTemplates",
                columns: new[] { "ServiceId", "TemplateKey", "TemplateVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedDocuments_DuplicateOfReceivedDocumentId",
                table: "ReceivedDocuments",
                column: "DuplicateOfReceivedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedDocuments_Sha256Hash",
                table: "ReceivedDocuments",
                column: "Sha256Hash");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedDocuments_SupersedesReceivedDocumentId",
                table: "ReceivedDocuments",
                column: "SupersedesReceivedDocumentId",
                unique: true,
                filter: "\"SupersedesReceivedDocumentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedDocumentStatusHistories_ReceivedDocumentId_Occurred~",
                table: "ReceivedDocumentStatusHistories",
                columns: new[] { "ReceivedDocumentId", "OccurredAt" });

            migrationBuilder.Sql("""
                CREATE FUNCTION "billing_validate_document_request_template_service"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_TABLE_NAME = 'DocumentRequests' THEN
                        IF NOT EXISTS
                        (
                            SELECT 1
                            FROM "WorkItems" wi
                            JOIN "BillingRecords" br ON br."Id" = wi."BillingRecordId"
                            JOIN "Engagements" e ON e."Id" = br."EngagementId"
                            JOIN "DocumentRequirementTemplates" t ON t."Id" = NEW."DocumentRequirementTemplateId"
                            WHERE wi."Id" = NEW."WorkItemId"
                              AND t."ServiceId" = e."ServiceId"
                        ) THEN
                            RAISE EXCEPTION 'Document request template service must match its work item billing engagement service'
                                USING ERRCODE = '23514';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'DocumentRequirementTemplates' THEN
                        IF TG_OP = 'UPDATE' AND NEW."ServiceId" IS NOT DISTINCT FROM OLD."ServiceId" THEN
                            RETURN NEW;
                        END IF;
                        IF EXISTS
                        (
                            SELECT 1
                            FROM "DocumentRequests" r
                            JOIN "WorkItems" wi ON wi."Id" = r."WorkItemId"
                            JOIN "BillingRecords" br ON br."Id" = wi."BillingRecordId"
                            JOIN "Engagements" e ON e."Id" = br."EngagementId"
                            WHERE r."DocumentRequirementTemplateId" = NEW."Id"
                              AND NEW."ServiceId" IS DISTINCT FROM e."ServiceId"
                        ) THEN
                            RAISE EXCEPTION 'Document request template service must match its work item billing engagement service'
                                USING ERRCODE = '23514';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'Engagements' THEN
                        IF TG_OP = 'UPDATE' AND NEW."ServiceId" IS NOT DISTINCT FROM OLD."ServiceId" THEN
                            RETURN NEW;
                        END IF;
                        IF EXISTS
                        (
                            SELECT 1
                            FROM "DocumentRequests" r
                            JOIN "WorkItems" wi ON wi."Id" = r."WorkItemId"
                            JOIN "BillingRecords" br ON br."Id" = wi."BillingRecordId"
                            JOIN "DocumentRequirementTemplates" t ON t."Id" = r."DocumentRequirementTemplateId"
                            WHERE br."EngagementId" = NEW."Id"
                              AND t."ServiceId" IS DISTINCT FROM NEW."ServiceId"
                        ) THEN
                            RAISE EXCEPTION 'Document request template service must match its work item billing engagement service'
                                USING ERRCODE = '23514';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'BillingRecords' THEN
                        IF TG_OP = 'UPDATE' AND NEW."EngagementId" IS NOT DISTINCT FROM OLD."EngagementId" THEN
                            RETURN NEW;
                        END IF;
                        IF EXISTS
                        (
                            SELECT 1
                            FROM "DocumentRequests" r
                            JOIN "WorkItems" wi ON wi."Id" = r."WorkItemId"
                            JOIN "DocumentRequirementTemplates" t ON t."Id" = r."DocumentRequirementTemplateId"
                            JOIN "Engagements" e ON e."Id" = NEW."EngagementId"
                            WHERE wi."BillingRecordId" = NEW."Id"
                              AND t."ServiceId" IS DISTINCT FROM e."ServiceId"
                        ) THEN
                            RAISE EXCEPTION 'Document request template service must match its work item billing engagement service'
                                USING ERRCODE = '23514';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'WorkItems' THEN
                        IF TG_OP = 'UPDATE' AND NEW."BillingRecordId" IS NOT DISTINCT FROM OLD."BillingRecordId" THEN
                            RETURN NEW;
                        END IF;
                        IF EXISTS
                        (
                            SELECT 1
                            FROM "DocumentRequests" r
                            JOIN "DocumentRequirementTemplates" t ON t."Id" = r."DocumentRequirementTemplateId"
                            JOIN "BillingRecords" br ON br."Id" = NEW."BillingRecordId"
                            JOIN "Engagements" e ON e."Id" = br."EngagementId"
                            WHERE r."WorkItemId" = NEW."Id"
                              AND t."ServiceId" IS DISTINCT FROM e."ServiceId"
                        ) THEN
                            RAISE EXCEPTION 'Document request template service must match its work item billing engagement service'
                                USING ERRCODE = '23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER "TR_DocumentRequests_TemplateServiceConsistency"
                AFTER INSERT OR UPDATE ON "DocumentRequests"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "billing_validate_document_request_template_service"();

                CREATE CONSTRAINT TRIGGER "TR_DocumentRequirementTemplates_TemplateServiceConsistency"
                AFTER UPDATE ON "DocumentRequirementTemplates"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "billing_validate_document_request_template_service"();

                CREATE CONSTRAINT TRIGGER "TR_Engagements_TemplateServiceConsistency"
                AFTER UPDATE ON "Engagements"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "billing_validate_document_request_template_service"();

                CREATE CONSTRAINT TRIGGER "TR_BillingRecords_TemplateServiceConsistency"
                AFTER UPDATE ON "BillingRecords"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "billing_validate_document_request_template_service"();

                CREATE CONSTRAINT TRIGGER "TR_WorkItems_TemplateServiceConsistency"
                AFTER UPDATE ON "WorkItems"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "billing_validate_document_request_template_service"();

                CREATE FUNCTION "billing_prevent_used_document_requirement_template_change"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM "DocumentRequests"
                        WHERE "DocumentRequirementTemplateId" = OLD."Id"
                    )
                    AND
                    (
                        NEW."ServiceId" IS DISTINCT FROM OLD."ServiceId"
                        OR NEW."TemplateKey" IS DISTINCT FROM OLD."TemplateKey"
                        OR NEW."TemplateVersion" IS DISTINCT FROM OLD."TemplateVersion"
                        OR NEW."Name" IS DISTINCT FROM OLD."Name"
                        OR NEW."Description" IS DISTINCT FROM OLD."Description"
                    ) THEN
                        RAISE EXCEPTION 'A document requirement template used by a request is immutable; create a new template version'
                            USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_DocumentRequirementTemplates_UsedDefinitionImmutable"
                BEFORE UPDATE ON "DocumentRequirementTemplates"
                FOR EACH ROW
                EXECUTE FUNCTION "billing_prevent_used_document_requirement_template_change"();

                CREATE FUNCTION "billing_prevent_used_document_requirement_template_item_change"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF EXISTS
                        (
                            SELECT 1
                            FROM "DocumentRequests"
                            WHERE "DocumentRequirementTemplateId" = NEW."DocumentRequirementTemplateId"
                        ) THEN
                            RAISE EXCEPTION 'A document requirement template used by a request cannot receive new checklist items; create a new template version'
                                USING ERRCODE = '55000';
                        END IF;
                        RETURN NEW;
                    ELSIF TG_OP = 'DELETE' THEN
                        IF EXISTS
                        (
                            SELECT 1
                            FROM "DocumentRequests"
                            WHERE "DocumentRequirementTemplateId" = OLD."DocumentRequirementTemplateId"
                        ) THEN
                            RAISE EXCEPTION 'A checklist item of a template used by a request is immutable; create a new template version'
                                USING ERRCODE = '55000';
                        END IF;
                        RETURN OLD;
                    END IF;

                    IF EXISTS
                    (
                        SELECT 1
                        FROM "DocumentRequests"
                        WHERE "DocumentRequirementTemplateId" IN (OLD."DocumentRequirementTemplateId", NEW."DocumentRequirementTemplateId")
                    )
                    AND
                    (
                        NEW."DocumentRequirementTemplateId" IS DISTINCT FROM OLD."DocumentRequirementTemplateId"
                        OR NEW."RequirementKey" IS DISTINCT FROM OLD."RequirementKey"
                        OR NEW."Name" IS DISTINCT FROM OLD."Name"
                        OR NEW."Description" IS DISTINCT FROM OLD."Description"
                        OR NEW."IsRequired" IS DISTINCT FROM OLD."IsRequired"
                        OR NEW."Wave" IS DISTINCT FROM OLD."Wave"
                        OR NEW."DisplayOrder" IS DISTINCT FROM OLD."DisplayOrder"
                    ) THEN
                        RAISE EXCEPTION 'A checklist item of a template used by a request is immutable; create a new template version'
                            USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER "TR_DocumentRequirementTemplateItems_UsedDefinitionImmutable"
                BEFORE INSERT OR UPDATE OR DELETE ON "DocumentRequirementTemplateItems"
                FOR EACH ROW
                EXECUTE FUNCTION "billing_prevent_used_document_requirement_template_item_change"();

                CREATE FUNCTION "billing_validate_document_request_lineage"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                DECLARE
                    parent_work_item_id integer;
                    parent_revision integer;
                BEGIN
                    IF NEW."SupersedesRequestId" IS NOT NULL THEN
                        SELECT "WorkItemId", "Revision"
                        INTO parent_work_item_id, parent_revision
                        FROM "DocumentRequests"
                        WHERE "Id" = NEW."SupersedesRequestId";

                        IF NOT FOUND OR NEW."WorkItemId" <> parent_work_item_id OR NEW."Revision" <> parent_revision + 1 THEN
                            RAISE EXCEPTION 'A replacement document request must use the same WorkItem and the next revision'
                                USING ERRCODE = '23514';
                        END IF;
                    END IF;

                    IF EXISTS
                    (
                        SELECT 1
                        FROM "DocumentRequests" child
                        WHERE child."SupersedesRequestId" = NEW."Id"
                          AND (child."WorkItemId" <> NEW."WorkItemId" OR child."Revision" <> NEW."Revision" + 1)
                    ) THEN
                        RAISE EXCEPTION 'A document request replacement lineage cannot branch or become inconsistent'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;

                CREATE CONSTRAINT TRIGGER "TR_DocumentRequests_SupersessionLineage"
                AFTER INSERT OR UPDATE ON "DocumentRequests"
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW
                EXECUTE FUNCTION "billing_validate_document_request_lineage"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS "TR_DocumentRequests_SupersessionLineage" ON "DocumentRequests";
                DROP TRIGGER IF EXISTS "TR_DocumentRequirementTemplateItems_UsedDefinitionImmutable" ON "DocumentRequirementTemplateItems";
                DROP TRIGGER IF EXISTS "TR_DocumentRequirementTemplates_UsedDefinitionImmutable" ON "DocumentRequirementTemplates";
                DROP TRIGGER IF EXISTS "TR_WorkItems_TemplateServiceConsistency" ON "WorkItems";
                DROP TRIGGER IF EXISTS "TR_BillingRecords_TemplateServiceConsistency" ON "BillingRecords";
                DROP TRIGGER IF EXISTS "TR_Engagements_TemplateServiceConsistency" ON "Engagements";
                DROP TRIGGER IF EXISTS "TR_DocumentRequirementTemplates_TemplateServiceConsistency" ON "DocumentRequirementTemplates";
                DROP TRIGGER IF EXISTS "TR_DocumentRequests_TemplateServiceConsistency" ON "DocumentRequests";
                DROP FUNCTION IF EXISTS "billing_validate_document_request_lineage"();
                DROP FUNCTION IF EXISTS "billing_prevent_used_document_requirement_template_item_change"();
                DROP FUNCTION IF EXISTS "billing_prevent_used_document_requirement_template_change"();
                DROP FUNCTION IF EXISTS "billing_validate_document_request_template_service"();
                """);

            migrationBuilder.DropTable(
                name: "DocumentRequestBatchMembers");

            migrationBuilder.DropTable(
                name: "DocumentRequestItemEvidenceHistories");

            migrationBuilder.DropTable(
                name: "DocumentRequestItemStatusHistories");

            migrationBuilder.DropTable(
                name: "DocumentRequestStatusHistories");

            migrationBuilder.DropTable(
                name: "ReceivedDocumentStatusHistories");

            migrationBuilder.DropTable(
                name: "DocumentRequestBatches");

            migrationBuilder.DropTable(
                name: "DocumentRequestItemEvidences");

            migrationBuilder.DropTable(
                name: "DocumentRequestItems");

            migrationBuilder.DropTable(
                name: "ReceivedDocuments");

            migrationBuilder.DropTable(
                name: "DocumentRequests");

            migrationBuilder.DropTable(
                name: "DocumentRequirementTemplateItems");

            migrationBuilder.DropTable(
                name: "DocumentRequirementTemplates");
        }
    }
}
