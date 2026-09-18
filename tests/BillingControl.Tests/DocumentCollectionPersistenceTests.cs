using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DomainRecord = BillingControl.Models.Record;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private sealed record DocumentFixture(int ServiceId, int EngagementId, int BillingRecordId, int WorkItemId);

    private static string DocumentToken(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task<Service> AddDocumentServiceAsync(AppDbContext db, string prefix)
    {
        var service = new Service { Name = DocumentToken(prefix) };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return service;
    }

    private static async Task<DocumentFixture> AddDocumentFixtureAsync(AppDbContext db, string prefix, int serviceId)
    {
        var engagement = new Engagement
        {
            Customer = new Customer { Name = DocumentToken($"customer-{prefix}") },
            ServiceId = serviceId,
            BusinessParty = new BusinessParty { Name = DocumentToken($"firm-{prefix}") },
            Manager = new Manager { Name = DocumentToken($"manager-{prefix}") },
            StartDate = new(2026, 1, 1),
            BillingAmount = 1000m,
            Schedule = new BillingSchedule
            {
                Frequency = Frequency.Monthly,
                NextPeriodStart = new(2026, 1, 1),
                AnchorDay = 1
            }
        };
        db.Engagements.Add(engagement);
        await db.SaveChangesAsync();

        var billing = new BillingRecord
        {
            EngagementId = engagement.Id,
            PeriodStart = new(2026, 1, 1),
            PeriodEnd = new(2026, 1, 31),
            Status = BillingStatus.Upcoming,
            RevenueShareBaseAmount = 1000m,
            Amount = 1000m,
            CustomerName = engagement.Customer.Name,
            ServiceName = $"Service-{prefix}"
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();

        var workItem = new WorkItem { BillingRecordId = billing.Id, Status = WorkStatus.Upcoming };
        db.WorkItems.Add(workItem);
        await db.SaveChangesAsync();
        return new(serviceId, engagement.Id, billing.Id, workItem.Id);
    }

    private static async Task<BillingRecord> AddDocumentBillingWithoutWorkItemAsync(AppDbContext db, string prefix, int engagementId)
    {
        var billing = new BillingRecord
        {
            EngagementId = engagementId,
            PeriodStart = new(2026, 2, 1),
            PeriodEnd = new(2026, 2, 28),
            Status = BillingStatus.Upcoming,
            RevenueShareBaseAmount = 1000m,
            Amount = 1000m,
            CustomerName = DocumentToken($"billing-customer-{prefix}"),
            ServiceName = $"Service-{prefix}"
        };
        db.BillingRecords.Add(billing);
        await db.SaveChangesAsync();
        return billing;
    }

    private static async Task<DocumentRequirementTemplate> AddDocumentTemplateAsync(
        AppDbContext db,
        int serviceId,
        string key,
        int version,
        bool isActive = true,
        bool isDefault = false)
    {
        var template = new DocumentRequirementTemplate
        {
            ServiceId = serviceId,
            TemplateKey = key,
            TemplateVersion = version,
            Name = $"Template {key} v{version}",
            Description = "Persistence test template",
            IsActive = isActive,
            IsDefault = isDefault
        };
        db.DocumentRequirementTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    private static async Task<DocumentRequirementTemplateItem> AddDocumentTemplateItemAsync(
        AppDbContext db,
        int templateId,
        string? key = null)
    {
        var item = new DocumentRequirementTemplateItem
        {
            DocumentRequirementTemplateId = templateId,
            RequirementKey = key ?? DocumentToken("requirement"),
            Name = "Bank statement",
            Description = "Latest statement",
            IsRequired = true,
            Wave = DocumentRequirementWave.Normal,
            DisplayOrder = 1,
            IsActive = true
        };
        db.DocumentRequirementTemplateItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<DocumentRequest> AddDocumentRequestAsync(
        AppDbContext db,
        int workItemId,
        int templateId,
        int revision = 1,
        DocumentRequestStatus status = DocumentRequestStatus.Draft,
        int? supersedesRequestId = null)
    {
        var request = new DocumentRequest
        {
            WorkItemId = workItemId,
            DocumentRequirementTemplateId = templateId,
            Revision = revision,
            Status = status,
            SupersedesRequestId = supersedesRequestId
        };
        db.DocumentRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static async Task<DocumentRequestItem> AddDocumentRequestItemAsync(
        AppDbContext db,
        int requestId,
        int templateItemId,
        string? key = null)
    {
        var item = new DocumentRequestItem
        {
            DocumentRequestId = requestId,
            DocumentRequirementTemplateItemId = templateItemId,
            RequirementKey = key ?? "bank-statement",
            RequirementName = "Bank statement",
            RequirementDescription = "Latest statement",
            IsRequired = true,
            Wave = DocumentRequirementWave.Normal,
            DisplayOrder = 1,
            Status = DocumentRequestItemStatus.Missing
        };
        db.DocumentRequestItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<ReceivedDocument> AddReceivedDocumentAsync(
        AppDbContext db,
        string prefix,
        long? byteLength = 16,
        string? sha256 = null,
        int? supersedesReceivedDocumentId = null)
    {
        var document = new ReceivedDocument
        {
            Status = ReceivedDocumentStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow,
            SenderSnapshot = $"sender-{prefix}",
            SourceSnapshot = $"source-{prefix}",
            OriginalFileName = $"{prefix}.pdf",
            MimeType = "application/pdf",
            ByteLength = byteLength,
            Sha256Hash = sha256 ?? new string('a', 64),
            SupersedesReceivedDocumentId = supersedesReceivedDocumentId
        };
        db.ReceivedDocuments.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private static async Task AssertDocumentPersistenceFailure(Func<Task> operation)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(operation);
        Assert.True(
            exception is InvalidOperationException or DbUpdateException or PostgresException or NpgsqlException,
            $"Unexpected persistence exception type: {exception.GetType().FullName}: {exception.Message}");
    }

    [PostgresFact]
    public async Task DocumentCollectionMigrationCreatesIsolatedSchema()
    {
        await using var db = await Fresh();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260911223353_DocumentCollectionPersistence", appliedMigrations);

        var tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('DocumentRequirementTemplates', 'DocumentRequirementTemplateItems', 'DocumentRequests', 'DocumentRequestItems', 'ReceivedDocuments', 'DocumentRequestItemEvidences', 'DocumentRequestBatches', 'DocumentRequestBatchMembers', 'DocumentRequestStatusHistories', 'DocumentRequestItemStatusHistories', 'ReceivedDocumentStatusHistories', 'DocumentRequestItemEvidenceHistories')
            """).ToListAsync();
        Assert.Equal(12, tables.Count);

        var receivedColumns = await db.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'ReceivedDocuments'
            """).ToListAsync();
        Assert.DoesNotContain("WorkItemId", receivedColumns);
        Assert.DoesNotContain("BillingRecordId", receivedColumns);
        Assert.DoesNotContain("DocumentRequestId", receivedColumns);
        Assert.DoesNotContain("DocumentRequestItemId", receivedColumns);

        var triggers = await db.Database.SqlQuery<string>($"""
            SELECT t.tgname AS "Value"
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            WHERE NOT t.tgisinternal
              AND c.relname IN ('DocumentRequirementTemplateItems', 'ReceivedDocuments')
            """).ToListAsync();
        Assert.Contains("TR_DocumentRequirementTemplateItems_UsedDefinitionImmutable", triggers);
        Assert.Contains("TR_ReceivedDocuments_Relationships", triggers);
        Assert.Contains("TR_ReceivedDocuments_SupersessionAcyclic", triggers);

        var acyclicFunction = await db.Database.SqlQuery<string>($"""
            SELECT pg_get_functiondef(p.oid) AS "Value"
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public'
              AND p.proname = 'billing_validate_received_document_supersession_acyclic'
            """).SingleAsync();
        Assert.Contains("WITH RECURSIVE", acyclicFunction, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task DocumentCollectionTemplateInvariantsAndUsedDefinitionsAreProtected()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "template-service");
        var key = DocumentToken("checklist");
        var historical = await AddDocumentTemplateAsync(db, service.Id, key, 1, isActive: false);
        var current = await AddDocumentTemplateAsync(db, service.Id, key, 2, isActive: true, isDefault: true);
        Assert.False(historical.IsActive);
        Assert.True(current.IsActive);
        Assert.True(current.IsDefault);

        async Task ExpectInvalidTemplate(DocumentRequirementTemplate template)
        {
            await using var invalid = Db();
            invalid.DocumentRequirementTemplates.Add(template);
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }

        await ExpectInvalidTemplate(new DocumentRequirementTemplate
        {
            ServiceId = service.Id,
            TemplateKey = DocumentToken("duplicate-key"),
            TemplateVersion = 0,
            Name = "Invalid version"
        });
        await ExpectInvalidTemplate(new DocumentRequirementTemplate
        {
            ServiceId = service.Id,
            TemplateKey = " ",
            TemplateVersion = 3,
            Name = "Valid name"
        });
        await ExpectInvalidTemplate(new DocumentRequirementTemplate
        {
            ServiceId = service.Id,
            TemplateKey = DocumentToken("blank-name"),
            TemplateVersion = 4,
            Name = ""
        });
        await ExpectInvalidTemplate(new DocumentRequirementTemplate
        {
            ServiceId = service.Id,
            TemplateKey = DocumentToken("inactive-default"),
            TemplateVersion = 5,
            Name = "Invalid default",
            IsActive = false,
            IsDefault = true
        });
        await ExpectInvalidTemplate(new DocumentRequirementTemplate
        {
            ServiceId = service.Id,
            TemplateKey = DocumentToken("second-default"),
            TemplateVersion = 6,
            Name = "Second default",
            IsActive = true,
            IsDefault = true
        });

        var fixture = await AddDocumentFixtureAsync(db, "used-template", service.Id);
        var item = await AddDocumentTemplateItemAsync(db, current.Id, "used-item");
        var request = await AddDocumentRequestAsync(db, fixture.WorkItemId, current.Id);

        await using (var lifecycle = Db())
        {
            var usedTemplate = await lifecycle.DocumentRequirementTemplates.SingleAsync(x => x.Id == current.Id);
            usedTemplate.IsDefault = false;
            await lifecycle.SaveChangesAsync();
        }

        await using (var guardedTemplate = Db())
        {
            var usedTemplate = await guardedTemplate.DocumentRequirementTemplates.SingleAsync(x => x.Id == current.Id);
            usedTemplate.Name = "Attempted rewrite";
            await Assert.ThrowsAsync<InvalidOperationException>(() => guardedTemplate.SaveChangesAsync());
        }

        var unusedTemplate = await AddDocumentTemplateAsync(db, service.Id, DocumentToken("unused-template"), 1, isActive: false);
        await using (var guardedItem = Db())
        {
            var usedItem = await guardedItem.DocumentRequirementTemplateItems.SingleAsync(x => x.Id == item.Id);
            usedItem.Name = "Attempted item rewrite";
            await Assert.ThrowsAsync<InvalidOperationException>(() => guardedItem.SaveChangesAsync());
        }
        await using (var guardedReparent = Db())
        {
            var usedItem = await guardedReparent.DocumentRequirementTemplateItems.SingleAsync(x => x.Id == item.Id);
            usedItem.DocumentRequirementTemplateId = unusedTemplate.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => guardedReparent.SaveChangesAsync());
        }
        await using (var guardedAdd = Db())
        {
            guardedAdd.DocumentRequirementTemplateItems.Add(new DocumentRequirementTemplateItem
            {
                DocumentRequirementTemplateId = current.Id,
                RequirementKey = "new-used-item",
                Name = "New item",
                IsRequired = false,
                Wave = DocumentRequirementWave.Later,
                DisplayOrder = 2,
                IsActive = true
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => guardedAdd.SaveChangesAsync());
        }

        var now = DateTime.UtcNow;
        await using (var directSql = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequirementTemplates\" SET \"Name\" = {"direct template rewrite"} WHERE \"Id\" = {current.Id}"));
        }
        await using (var directSql = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequirementTemplateItems\" SET \"Name\" = {"direct item rewrite"} WHERE \"Id\" = {item.Id}"));
        }
        await using (var directSql = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequirementTemplateItems\" SET \"IsActive\" = FALSE WHERE \"Id\" = {item.Id}"));
        }
        await using (var directSql = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequirementTemplateItems\" SET \"DocumentRequirementTemplateId\" = {unusedTemplate.Id} WHERE \"Id\" = {item.Id}"));
        }
        await using (var directSql = Db())
        {
            string? description = null;
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"DocumentRequirementTemplateItems\" (\"DocumentRequirementTemplateId\", \"RequirementKey\", \"Name\", \"Description\", \"IsRequired\", \"Wave\", \"DisplayOrder\", \"IsActive\", \"CreatedAt\", \"CreatedBy\", \"UpdatedAt\", \"UpdatedBy\", \"Version\") VALUES ({current.Id}, {"direct-added-item"}, {"Direct item"}, {description}, {false}, {(int)DocumentRequirementWave.Normal}, {2}, {true}, {now}, {"test"}, {now}, {"test"}, {1})"));
        }
        await using (var directSql = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"DocumentRequirementTemplateItems\" WHERE \"Id\" = {item.Id}"));
        }
    }

    [PostgresFact]
    public async Task DocumentCollectionTemplateServiceConsistencyIsProtectedAcrossReferences()
    {
        await using var db = await Fresh();
        var serviceA = await AddDocumentServiceAsync(db, "service-a");
        var serviceB = await AddDocumentServiceAsync(db, "service-b");
        var fixtureA = await AddDocumentFixtureAsync(db, "consistency-a", serviceA.Id);
        var fixtureA2 = await AddDocumentFixtureAsync(db, "consistency-a2", serviceA.Id);
        var fixtureB = await AddDocumentFixtureAsync(db, "consistency-b", serviceB.Id);
        var templateA = await AddDocumentTemplateAsync(db, serviceA.Id, DocumentToken("service-a-template"), 1);
        var templateB = await AddDocumentTemplateAsync(db, serviceB.Id, DocumentToken("service-b-template"), 1);
        var validRequest = await AddDocumentRequestAsync(db, fixtureA.WorkItemId, templateA.Id);
        var unusedBillingB = await AddDocumentBillingWithoutWorkItemAsync(db, "consistency-b-unused", fixtureB.EngagementId);

        await using (var mismatched = Db())
        {
            mismatched.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = fixtureA2.WorkItemId,
                DocumentRequirementTemplateId = templateB.Id,
                Revision = 1,
                Status = DocumentRequestStatus.Draft
            });
            await AssertDocumentPersistenceFailure(() => mismatched.SaveChangesAsync());
        }

        async Task ExpectDirectMismatch(FormattableString sql)
        {
            await using var directSql = Db();
            await AssertDocumentPersistenceFailure(() => directSql.Database.ExecuteSqlInterpolatedAsync(sql));
        }

        await ExpectDirectMismatch($"UPDATE \"DocumentRequirementTemplates\" SET \"ServiceId\" = {serviceB.Id} WHERE \"Id\" = {templateA.Id}");
        await ExpectDirectMismatch($"UPDATE \"Engagements\" SET \"ServiceId\" = {serviceB.Id} WHERE \"Id\" = {fixtureA.EngagementId}");
        await ExpectDirectMismatch($"UPDATE \"BillingRecords\" SET \"EngagementId\" = {fixtureB.EngagementId} WHERE \"Id\" = {fixtureA.BillingRecordId}");
        await ExpectDirectMismatch($"UPDATE \"WorkItems\" SET \"BillingRecordId\" = {unusedBillingB.Id} WHERE \"Id\" = {fixtureA.WorkItemId}");

        var remainsConsistent = await db.Database.SqlQuery<bool>($"""
            SELECT (t."ServiceId" = e."ServiceId") AS "Value"
            FROM "DocumentRequests" r
            JOIN "WorkItems" wi ON wi."Id" = r."WorkItemId"
            JOIN "BillingRecords" br ON br."Id" = wi."BillingRecordId"
            JOIN "Engagements" e ON e."Id" = br."EngagementId"
            JOIN "DocumentRequirementTemplates" t ON t."Id" = r."DocumentRequirementTemplateId"
            WHERE r."Id" = {validRequest.Id}
            """).SingleAsync();
        Assert.True(remainsConsistent);
    }

    [PostgresFact]
    public async Task DocumentCollectionRequestInvariantsAndLineageAreProtected()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "request-service");
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken("request-template"), 1);

        var currentFixture = await AddDocumentFixtureAsync(db, "request-current", service.Id);
        var first = await AddDocumentRequestAsync(db, currentFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Draft);
        first.Status = DocumentRequestStatus.Cancelled;
        await db.SaveChangesAsync();
        var second = await AddDocumentRequestAsync(db, currentFixture.WorkItemId, template.Id, 2, DocumentRequestStatus.Draft);
        second.Status = DocumentRequestStatus.Superseded;
        await db.SaveChangesAsync();
        var third = await AddDocumentRequestAsync(db, currentFixture.WorkItemId, template.Id, 3, DocumentRequestStatus.Draft);
        Assert.Equal(DocumentRequestStatus.Draft, third.Status);

        var invalidRevisionFixture = await AddDocumentFixtureAsync(db, "request-invalid-revision", service.Id);
        await using (var invalid = Db())
        {
            invalid.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = invalidRevisionFixture.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = 0,
                Status = DocumentRequestStatus.Cancelled
            });
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }
        await AddDocumentRequestAsync(db, invalidRevisionFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Cancelled);
        await using (var duplicateRevision = Db())
        {
            duplicateRevision.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = invalidRevisionFixture.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = 1,
                Status = DocumentRequestStatus.Cancelled
            });
            await AssertDocumentPersistenceFailure(() => duplicateRevision.SaveChangesAsync());
        }

        var currentIndexFixture = await AddDocumentFixtureAsync(db, "request-current-index", service.Id);
        var current = await AddDocumentRequestAsync(db, currentIndexFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Draft);
        await using (var secondCurrent = Db())
        {
            secondCurrent.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = currentIndexFixture.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = 2,
                Status = DocumentRequestStatus.Requested
            });
            await AssertDocumentPersistenceFailure(() => secondCurrent.SaveChangesAsync());
        }
        current.Status = DocumentRequestStatus.Cancelled;
        await db.SaveChangesAsync();
        var afterCancellation = await AddDocumentRequestAsync(db, currentIndexFixture.WorkItemId, template.Id, 2, DocumentRequestStatus.Requested);
        afterCancellation.Status = DocumentRequestStatus.Superseded;
        await db.SaveChangesAsync();
        await AddDocumentRequestAsync(db, currentIndexFixture.WorkItemId, template.Id, 3, DocumentRequestStatus.PartiallyReceived);

        await using (var invalidStatus = Db())
        {
            await AssertDocumentPersistenceFailure(() => invalidStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequests\" SET \"Status\" = {99} WHERE \"Id\" = {third.Id}"));
        }

        var validLineageFixture = await AddDocumentFixtureAsync(db, "request-lineage-valid", service.Id);
        var lineageParent = await AddDocumentRequestAsync(db, validLineageFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Draft);
        lineageParent.Status = DocumentRequestStatus.Superseded;
        await db.SaveChangesAsync();
        var lineageChild = await AddDocumentRequestAsync(db, validLineageFixture.WorkItemId, template.Id, 2, DocumentRequestStatus.Draft, lineageParent.Id);
        Assert.Equal(lineageParent.Id, lineageChild.SupersedesRequestId);

        var wrongWorkItemFixture = await AddDocumentFixtureAsync(db, "request-lineage-wrong-work", service.Id);
        var wrongParent = await AddDocumentRequestAsync(db, wrongWorkItemFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Superseded);
        var otherWorkItem = await AddDocumentFixtureAsync(db, "request-lineage-other-work", service.Id);
        await using (var differentWorkItem = Db())
        {
            differentWorkItem.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = otherWorkItem.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = 2,
                Status = DocumentRequestStatus.Draft,
                SupersedesRequestId = wrongParent.Id
            });
            await AssertDocumentPersistenceFailure(() => differentWorkItem.SaveChangesAsync());
        }

        var skippedFixture = await AddDocumentFixtureAsync(db, "request-lineage-skipped", service.Id);
        var skippedParent = await AddDocumentRequestAsync(db, skippedFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Superseded);
        await using (var skippedRevision = Db())
        {
            skippedRevision.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = skippedFixture.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = 3,
                Status = DocumentRequestStatus.Draft,
                SupersedesRequestId = skippedParent.Id
            });
            await AssertDocumentPersistenceFailure(() => skippedRevision.SaveChangesAsync());
        }

        var successorFixture = await AddDocumentFixtureAsync(db, "request-lineage-successor", service.Id);
        var successorParent = await AddDocumentRequestAsync(db, successorFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Superseded);
        await AddDocumentRequestAsync(db, successorFixture.WorkItemId, template.Id, 2, DocumentRequestStatus.Draft, successorParent.Id);
        await using (var branched = Db())
        {
            branched.DocumentRequests.Add(new DocumentRequest
            {
                WorkItemId = successorFixture.WorkItemId,
                DocumentRequirementTemplateId = template.Id,
                Revision = 2,
                Status = DocumentRequestStatus.Draft,
                SupersedesRequestId = successorParent.Id
            });
            await AssertDocumentPersistenceFailure(() => branched.SaveChangesAsync());
        }

        var selfFixture = await AddDocumentFixtureAsync(db, "request-lineage-self", service.Id);
        var selfRequest = await AddDocumentRequestAsync(db, selfFixture.WorkItemId, template.Id, 1, DocumentRequestStatus.Cancelled);
        await using (var self = Db())
        {
            await AssertDocumentPersistenceFailure(() => self.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequests\" SET \"SupersedesRequestId\" = \"Id\" WHERE \"Id\" = {selfRequest.Id}"));
        }
    }

    [PostgresFact]
    public async Task DocumentCollectionRequestItemInvariantsAndSnapshotsAreProtected()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "item-service");
        var fixture = await AddDocumentFixtureAsync(db, "item-primary", service.Id);
        var otherFixture = await AddDocumentFixtureAsync(db, "item-other", service.Id);
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken("item-template"), 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "item-key");
        var request = await AddDocumentRequestAsync(db, fixture.WorkItemId, template.Id);
        var otherRequest = await AddDocumentRequestAsync(db, otherFixture.WorkItemId, template.Id, status: DocumentRequestStatus.Cancelled);
        var item = await AddDocumentRequestItemAsync(db, request.Id, templateItem.Id);

        await using (var duplicate = Db())
        {
            duplicate.DocumentRequestItems.Add(new DocumentRequestItem
            {
                DocumentRequestId = request.Id,
                DocumentRequirementTemplateItemId = templateItem.Id,
                RequirementKey = item.RequirementKey,
                RequirementName = "Duplicate key",
                IsRequired = true,
                Wave = DocumentRequirementWave.Normal,
                DisplayOrder = 2
            });
            await AssertDocumentPersistenceFailure(() => duplicate.SaveChangesAsync());
        }

        var frozenMutations = new (string Name, Action<DocumentRequestItem> Apply)[]
        {
            ("DocumentRequestId", x => x.DocumentRequestId = otherRequest.Id),
            ("DocumentRequirementTemplateItemId", x => x.DocumentRequirementTemplateItemId = null),
            ("RequirementKey", x => x.RequirementKey = "replacement-key"),
            ("RequirementName", x => x.RequirementName = "Replacement name"),
            ("RequirementDescription", x => x.RequirementDescription = "Replacement description"),
            ("IsRequired", x => x.IsRequired = !x.IsRequired),
            ("Wave", x => x.Wave = DocumentRequirementWave.Later),
            ("DisplayOrder", x => x.DisplayOrder = x.DisplayOrder + 1)
        };
        foreach (var mutation in frozenMutations)
        {
            await using var candidate = Db();
            var tracked = await candidate.DocumentRequestItems.SingleAsync(x => x.Id == item.Id);
            mutation.Apply(tracked);
            await Assert.ThrowsAsync<InvalidOperationException>(() => candidate.SaveChangesAsync());
        }

        await using (var statusChange = Db())
        {
            var tracked = await statusChange.DocumentRequestItems.SingleAsync(x => x.Id == item.Id);
            tracked.Status = DocumentRequestItemStatus.Requested;
            await statusChange.SaveChangesAsync();
        }

        await using (var blankKey = Db())
        {
            await AssertDocumentPersistenceFailure(() => blankKey.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItems\" SET \"RequirementKey\" = {" "} WHERE \"Id\" = {item.Id}"));
        }
        await using (var blankName = Db())
        {
            await AssertDocumentPersistenceFailure(() => blankName.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItems\" SET \"RequirementName\" = {" "} WHERE \"Id\" = {item.Id}"));
        }
        await using (var invalidWave = Db())
        {
            await AssertDocumentPersistenceFailure(() => invalidWave.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItems\" SET \"Wave\" = {99} WHERE \"Id\" = {item.Id}"));
        }
        await using (var invalidStatus = Db())
        {
            await AssertDocumentPersistenceFailure(() => invalidStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItems\" SET \"Status\" = {99} WHERE \"Id\" = {item.Id}"));
        }
    }

    [PostgresFact]
    public async Task ReceivedDocumentRawDuplicateAndReplacementInvariantsAreProtected()
    {
        await using var db = await Fresh();
        var canonical = await AddReceivedDocumentAsync(db, "canonical");
        var replacement = await AddReceivedDocumentAsync(db, "replacement", supersedesReceivedDocumentId: canonical.Id);
        var secondCanonical = await AddReceivedDocumentAsync(db, "second-canonical");
        var replacementChild = await AddReceivedDocumentAsync(db, "replacement-child", supersedesReceivedDocumentId: replacement.Id);
        Assert.Equal(canonical.Id, replacement.SupersedesReceivedDocumentId);
        Assert.Equal(replacement.Id, replacementChild.SupersedesReceivedDocumentId);
        Assert.Equal(0, await db.DocumentRequestItemEvidences.CountAsync(x => x.ReceivedDocumentId == canonical.Id));

        async Task ExpectInvalidReceivedDocument(long byteLength, string sha256)
        {
            await using var invalid = Db();
            invalid.ReceivedDocuments.Add(new ReceivedDocument
            {
                Status = ReceivedDocumentStatus.PendingReview,
                ReceivedAt = DateTime.UtcNow,
                SenderSnapshot = "invalid-sender",
                SourceSnapshot = "invalid-source",
                OriginalFileName = "invalid.pdf",
                MimeType = "application/pdf",
                ByteLength = byteLength,
                Sha256Hash = sha256
            });
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }
        await ExpectInvalidReceivedDocument(0, new string('b', 64));
        await ExpectInvalidReceivedDocument(-1, new string('b', 64));
        await ExpectInvalidReceivedDocument(16, "not-a-sha256");

        await using (var guardedMetadata = Db())
        {
            var tracked = await guardedMetadata.ReceivedDocuments.SingleAsync(x => x.Id == canonical.Id);
            tracked.OriginalFileName = "changed.pdf";
            await Assert.ThrowsAsync<InvalidOperationException>(() => guardedMetadata.SaveChangesAsync());
        }
        await using (var directMetadata = Db())
        {
            await AssertDocumentPersistenceFailure(() => directMetadata.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"ReceivedAt\" = {DateTime.UtcNow.AddDays(1)} WHERE \"Id\" = {canonical.Id}"));
        }
        await using (var repointed = Db())
        {
            var tracked = await repointed.ReceivedDocuments.SingleAsync(x => x.Id == replacement.Id);
            tracked.SupersedesReceivedDocumentId = secondCanonical.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => repointed.SaveChangesAsync());
        }
        await using (var directRepoint = Db())
        {
            await AssertDocumentPersistenceFailure(() => directRepoint.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"SupersedesReceivedDocumentId\" = {secondCanonical.Id} WHERE \"Id\" = {replacement.Id}"));
        }
        await using (var secondChild = Db())
        {
            secondChild.ReceivedDocuments.Add(new ReceivedDocument
            {
                Status = ReceivedDocumentStatus.PendingReview,
                ReceivedAt = DateTime.UtcNow,
                SenderSnapshot = "second-child",
                SourceSnapshot = "test",
                OriginalFileName = "second-child.pdf",
                MimeType = "application/pdf",
                ByteLength = 16,
                Sha256Hash = new string('c', 64),
                SupersedesReceivedDocumentId = canonical.Id
            });
            await AssertDocumentPersistenceFailure(() => secondChild.SaveChangesAsync());
        }

        var duplicateOne = await AddReceivedDocumentAsync(db, "duplicate-one");
        var duplicateTwo = await AddReceivedDocumentAsync(db, "duplicate-two");
        await using (var classify = Db())
        {
            var tracked = await classify.ReceivedDocuments.SingleAsync(x => x.Id == duplicateOne.Id);
            tracked.Status = ReceivedDocumentStatus.Duplicate;
            tracked.DuplicateOfReceivedDocumentId = canonical.Id;
            await classify.SaveChangesAsync();
        }
        await using (var classify = Db())
        {
            var tracked = await classify.ReceivedDocuments.SingleAsync(x => x.Id == duplicateTwo.Id);
            tracked.Status = ReceivedDocumentStatus.Duplicate;
            tracked.DuplicateOfReceivedDocumentId = canonical.Id;
            await classify.SaveChangesAsync();
        }

        await using (var clearCanonical = Db())
        {
            await AssertDocumentPersistenceFailure(() => clearCanonical.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"DuplicateOfReceivedDocumentId\" = NULL WHERE \"Id\" = {duplicateOne.Id}"));
        }
        await using (var terminalStatus = Db())
        {
            await AssertDocumentPersistenceFailure(() => terminalStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"Status\" = {(int)ReceivedDocumentStatus.Accepted} WHERE \"Id\" = {duplicateOne.Id}"));
        }

        var duplicateOfDuplicate = await AddReceivedDocumentAsync(db, "duplicate-of-duplicate");
        await using (var directDuplicateOfDuplicate = Db())
        {
            await AssertDocumentPersistenceFailure(() => directDuplicateOfDuplicate.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"Status\" = {(int)ReceivedDocumentStatus.Duplicate}, \"DuplicateOfReceivedDocumentId\" = {duplicateOne.Id} WHERE \"Id\" = {duplicateOfDuplicate.Id}"));
        }
        var selfDuplicate = await AddReceivedDocumentAsync(db, "self-duplicate");
        await using (var directSelfDuplicate = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSelfDuplicate.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"Status\" = {(int)ReceivedDocumentStatus.Duplicate}, \"DuplicateOfReceivedDocumentId\" = \"Id\" WHERE \"Id\" = {selfDuplicate.Id}"));
        }
        Assert.Equal(2, await db.ReceivedDocuments.CountAsync(x => x.DuplicateOfReceivedDocumentId == canonical.Id));

        var selfReplacement = await AddReceivedDocumentAsync(db, "self-replacement");
        await using (var directSelfReplacement = Db())
        {
            await AssertDocumentPersistenceFailure(() => directSelfReplacement.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocuments\" SET \"SupersedesReceivedDocumentId\" = \"Id\" WHERE \"Id\" = {selfReplacement.Id}"));
        }
    }

    [PostgresFact]
    public async Task DocumentCollectionEvidenceInvariantsAreProtected()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "evidence-service");
        var fixture = await AddDocumentFixtureAsync(db, "evidence-fixture", service.Id);
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken("evidence-template"), 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "evidence-item");
        var request = await AddDocumentRequestAsync(db, fixture.WorkItemId, template.Id);
        var requestItem = await AddDocumentRequestItemAsync(db, request.Id, templateItem.Id);
        var raw = await AddReceivedDocumentAsync(db, "evidence-raw");
        var secondRaw = await AddReceivedDocumentAsync(db, "evidence-second-raw");
        var evidence = new DocumentRequestItemEvidence
        {
            DocumentRequestItemId = requestItem.Id,
            ReceivedDocumentId = raw.Id,
            IsActive = true
        };
        db.DocumentRequestItemEvidences.Add(evidence);
        await db.SaveChangesAsync();

        await using (var duplicate = Db())
        {
            duplicate.DocumentRequestItemEvidences.Add(new DocumentRequestItemEvidence
            {
                DocumentRequestItemId = requestItem.Id,
                ReceivedDocumentId = raw.Id,
                IsActive = true
            });
            await AssertDocumentPersistenceFailure(() => duplicate.SaveChangesAsync());
        }
        await using (var activeTimestamp = Db())
        {
            await AssertDocumentPersistenceFailure(() => activeTimestamp.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItemEvidences\" SET \"InactivatedAt\" = {DateTime.UtcNow} WHERE \"Id\" = {evidence.Id}"));
        }
        await using (var inactiveMissingReason = Db())
        {
            await AssertDocumentPersistenceFailure(() => inactiveMissingReason.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItemEvidences\" SET \"IsActive\" = FALSE WHERE \"Id\" = {evidence.Id}"));
        }
        await using (var inactiveBlankReason = Db())
        {
            await AssertDocumentPersistenceFailure(() => inactiveBlankReason.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItemEvidences\" SET \"IsActive\" = FALSE, \"InactivatedAt\" = {DateTime.UtcNow}, \"InactivationReason\" = {" "} WHERE \"Id\" = {evidence.Id}"));
        }
        await using (var inactive = Db())
        {
            var tracked = await inactive.DocumentRequestItemEvidences.SingleAsync(x => x.Id == evidence.Id);
            tracked.IsActive = false;
            tracked.InactivatedAt = DateTime.UtcNow;
            tracked.InactivationReason = "Explicit test inactivation";
            await inactive.SaveChangesAsync();
        }
        await using (var reactivated = Db())
        {
            var tracked = await reactivated.DocumentRequestItemEvidences.SingleAsync(x => x.Id == evidence.Id);
            tracked.IsActive = true;
            tracked.InactivatedAt = null;
            tracked.InactivationReason = null;
            await reactivated.SaveChangesAsync();
        }
        await using (var identity = Db())
        {
            var tracked = await identity.DocumentRequestItemEvidences.SingleAsync(x => x.Id == evidence.Id);
            tracked.ReceivedDocumentId = secondRaw.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => identity.SaveChangesAsync());
        }
        Assert.Equal(0, await db.DocumentRequestItemEvidences.CountAsync(x => x.ReceivedDocumentId == secondRaw.Id));
    }

    [PostgresFact]
    public async Task DocumentCollectionHistoryIsAppendOnlyAndValidated()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "history-service");
        var fixture = await AddDocumentFixtureAsync(db, "history-fixture", service.Id);
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken("history-template"), 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, "history-item");
        var request = await AddDocumentRequestAsync(db, fixture.WorkItemId, template.Id);
        var requestItem = await AddDocumentRequestItemAsync(db, request.Id, templateItem.Id);
        var received = await AddReceivedDocumentAsync(db, "history-received");
        var evidence = new DocumentRequestItemEvidence { DocumentRequestItemId = requestItem.Id, ReceivedDocumentId = received.Id };
        db.DocumentRequestItemEvidences.Add(evidence);
        await db.SaveChangesAsync();
        var occurred = DateTime.UtcNow;
        var requestHistory = new DocumentRequestStatusHistory
        {
            DocumentRequestId = request.Id,
            PreviousStatus = null,
            NewStatus = DocumentRequestStatus.Draft,
            Action = "Created",
            Actor = "tester",
            Source = "integration",
            OccurredAt = occurred
        };
        var itemHistory = new DocumentRequestItemStatusHistory
        {
            DocumentRequestItemId = requestItem.Id,
            PreviousStatus = null,
            NewStatus = DocumentRequestItemStatus.Missing,
            Action = "Created",
            Actor = "tester",
            Source = "integration",
            OccurredAt = occurred
        };
        var receivedHistory = new ReceivedDocumentStatusHistory
        {
            ReceivedDocumentId = received.Id,
            PreviousStatus = null,
            NewStatus = ReceivedDocumentStatus.PendingReview,
            Action = "Received",
            Actor = "tester",
            Source = "integration",
            OccurredAt = occurred
        };
        var evidenceHistory = new DocumentRequestItemEvidenceHistory
        {
            DocumentRequestItemEvidenceId = evidence.Id,
            PreviousIsActive = null,
            NewIsActive = true,
            Action = "Linked",
            Actor = "tester",
            Source = "integration",
            OccurredAt = occurred
        };
        db.AddRange(requestHistory, itemHistory, receivedHistory, evidenceHistory);
        await db.SaveChangesAsync();

        async Task AssertHistoryModificationFails<T>(Func<AppDbContext, IQueryable<T>> query, Action<T> mutate) where T : DomainRecord
        {
            await using var context = Db();
            var row = await query(context).SingleAsync();
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        async Task AssertHistoryDeletionFails<T>(Func<AppDbContext, IQueryable<T>> query) where T : DomainRecord
        {
            await using var context = Db();
            var row = await query(context).SingleAsync();
            context.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertHistoryModificationFails(c => c.DocumentRequestStatusHistories.Where(x => x.Id == requestHistory.Id), x => x.Action = "Changed");
        await AssertHistoryModificationFails(c => c.DocumentRequestItemStatusHistories.Where(x => x.Id == itemHistory.Id), x => x.Action = "Changed");
        await AssertHistoryModificationFails(c => c.ReceivedDocumentStatusHistories.Where(x => x.Id == receivedHistory.Id), x => x.Action = "Changed");
        await AssertHistoryModificationFails(c => c.DocumentRequestItemEvidenceHistories.Where(x => x.Id == evidenceHistory.Id), x => x.Action = "Changed");
        await AssertHistoryDeletionFails(c => c.DocumentRequestStatusHistories.Where(x => x.Id == requestHistory.Id));
        await AssertHistoryDeletionFails(c => c.DocumentRequestItemStatusHistories.Where(x => x.Id == itemHistory.Id));
        await AssertHistoryDeletionFails(c => c.ReceivedDocumentStatusHistories.Where(x => x.Id == receivedHistory.Id));
        await AssertHistoryDeletionFails(c => c.DocumentRequestItemEvidenceHistories.Where(x => x.Id == evidenceHistory.Id));

        var historyRows = new (string Table, int Id)[]
        {
            ("DocumentRequestStatusHistories", requestHistory.Id),
            ("DocumentRequestItemStatusHistories", itemHistory.Id),
            ("ReceivedDocumentStatusHistories", receivedHistory.Id),
            ("DocumentRequestItemEvidenceHistories", evidenceHistory.Id)
        };
        foreach (var history in historyRows)
        {
            foreach (var column in new[] { "Action", "Actor", "Source" })
            {
                await using var blank = Db();
                var sql = $"UPDATE \"{history.Table}\" SET \"{column}\" = ' ' WHERE \"Id\" = {history.Id}";
                await AssertDocumentPersistenceFailure(() => blank.Database.ExecuteSqlRawAsync(sql));
            }
        }
        await using (var invalidRequestStatus = Db())
        {
            await AssertDocumentPersistenceFailure(() => invalidRequestStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestStatusHistories\" SET \"NewStatus\" = {99} WHERE \"Id\" = {requestHistory.Id}"));
        }
        await using (var invalidItemStatus = Db())
        {
            await AssertDocumentPersistenceFailure(() => invalidItemStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestItemStatusHistories\" SET \"NewStatus\" = {99} WHERE \"Id\" = {itemHistory.Id}"));
        }
        await using (var invalidReceivedStatus = Db())
        {
            await AssertDocumentPersistenceFailure(() => invalidReceivedStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ReceivedDocumentStatusHistories\" SET \"NewStatus\" = {99} WHERE \"Id\" = {receivedHistory.Id}"));
        }
    }

    [PostgresFact]
    public async Task DocumentCollectionRecordVersionIsOptimisticConcurrencyToken()
    {
        await using var db = await Fresh();
        var service = await AddDocumentServiceAsync(db, "concurrency-service");
        var fixture = await AddDocumentFixtureAsync(db, "concurrency-fixture", service.Id);
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken("concurrency-template"), 1);
        var request = await AddDocumentRequestAsync(db, fixture.WorkItemId, template.Id);
        var received = await AddReceivedDocumentAsync(db, "concurrency-received");

        await using (var first = Db())
        await using (var second = Db())
        {
            var firstRequest = await first.DocumentRequests.SingleAsync(x => x.Id == request.Id);
            var secondRequest = await second.DocumentRequests.SingleAsync(x => x.Id == request.Id);
            firstRequest.Status = DocumentRequestStatus.Paused;
            await first.SaveChangesAsync();
            secondRequest.Status = DocumentRequestStatus.Requested;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }

        await using (var first = Db())
        await using (var second = Db())
        {
            var firstReceived = await first.ReceivedDocuments.SingleAsync(x => x.Id == received.Id);
            var secondReceived = await second.ReceivedDocuments.SingleAsync(x => x.Id == received.Id);
            firstReceived.Status = ReceivedDocumentStatus.Quarantined;
            await first.SaveChangesAsync();
            secondReceived.Status = ReceivedDocumentStatus.Rejected;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
    }
}
