using BillingControl.Data;
using BillingControl.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using DomainRecord = BillingControl.Models.Record;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private sealed record OutboundFixture(
        int ContactId,
        int AddressId,
        int ConversationId,
        int ParticipantId,
        int EngagementId,
        int CustomerId,
        int ServiceId,
        int BusinessPartyId,
        int ScopeId,
        int BatchId,
        int BatchSnapshotId,
        int RequestId,
        int ItemId,
        int MessageId,
        string LogicalMessageKey);

    private static async Task<OutboundFixture> AddOutboundFixtureAsync(
        AppDbContext db,
        string prefix,
        bool includeMessage = true,
        WhatsAppOutboundContentKind contentKind = WhatsAppOutboundContentKind.Text)
    {
        var service = await AddDocumentServiceAsync(db, $"outbound-service-{prefix}");
        var documentFixture = await AddDocumentFixtureAsync(db, $"outbound-{prefix}", service.Id);
        var template = await AddDocumentTemplateAsync(db, service.Id, DocumentToken($"outbound-template-{prefix}"), 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, DocumentToken($"outbound-requirement-{prefix}"));
        var request = await AddDocumentRequestAsync(db, documentFixture.WorkItemId, template.Id);
        var requestItem = await AddDocumentRequestItemAsync(db, request.Id, templateItem.Id);

        var contact = await AddContactAsync(db, $"outbound-contact-{prefix}");
        var address = await AddContactAddressAsync(db, contact.Id, $"+6013{Random.Shared.Next(10_000_000, 100_000_000)}");
        var conversation = await AddGroupConversationAsync(
            db,
            $"outbound-conversation-{prefix}",
            businessEndpointKey: $"outbound-endpoint-{Guid.NewGuid():N}");
        var participant = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
            ProviderParticipantKey = $"provider-participant-{Guid.NewGuid():N}",
            DisplayNameSnapshot = "Provider participant",
            JoinedAt = ConversationTestTime
        };
        db.WhatsAppConversationParticipants.Add(participant);
        await db.SaveChangesAsync();

        var engagement = await db.Engagements
            .Include(x => x.Customer)
            .Include(x => x.Service)
            .Include(x => x.BusinessParty)
            .SingleAsync(x => x.Id == documentFixture.EngagementId);
        var scope = new WhatsAppConversationEngagementScope
        {
            WhatsAppConversationId = conversation.Id,
            EngagementId = engagement.Id,
            ApprovedAuthorizationVersion = 1,
            ApprovedAt = ConversationTestTime,
            ApprovedByActor = "phase10a-test",
            ApprovalReason = "Persistence fixture"
        };
        db.WhatsAppConversationEngagementScopes.Add(scope);
        await db.SaveChangesAsync();

        var batch = new DocumentRequestBatch();
        db.DocumentRequestBatches.Add(batch);
        await db.SaveChangesAsync();

        var batchSnapshot = new WhatsAppOutboundBatchSnapshot
        {
            DocumentRequestBatchId = batch.Id,
            ContactId = contact.Id,
            WhatsAppConversationId = conversation.Id,
            ConversationAuthorizationVersion = 1,
            ParticipantSetHash = new string('a', 64),
            QueuedAt = ConversationTestTime,
            QueuedByActor = "phase10a-test",
            CorrelationId = $"batch-correlation-{Guid.NewGuid():N}"
        };
        db.WhatsAppOutboundBatchSnapshots.Add(batchSnapshot);
        await db.SaveChangesAsync();

        var requestSnapshot = new WhatsAppOutboundRequestSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = batchSnapshot.Id,
            DocumentRequestId = request.Id,
            RequestRevision = request.Revision,
            RequestVersion = request.Version,
            EngagementId = engagement.Id,
            CustomerId = engagement.CustomerId,
            CustomerNameSnapshot = engagement.Customer.Name,
            ServiceId = engagement.ServiceId,
            ServiceNameSnapshot = engagement.Service.Name
        };
        var itemSnapshot = new WhatsAppOutboundItemSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = batchSnapshot.Id,
            DocumentRequestId = request.Id,
            DocumentRequestItemId = requestItem.Id,
            RequestRevision = request.Revision,
            RequirementNameSnapshot = requestItem.RequirementName,
            IsRequired = requestItem.IsRequired,
            DisplayOrder = requestItem.DisplayOrder,
            ItemStatusSnapshot = requestItem.Status
        };
        var participantSnapshot = new WhatsAppOutboundParticipantSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = batchSnapshot.Id,
            WhatsAppConversationParticipantId = participant.Id,
            ParticipantKind = participant.ParticipantKind,
            ProviderParticipantKey = participant.ProviderParticipantKey,
            DisplayNameSnapshot = participant.DisplayNameSnapshot
        };
        var scopeSnapshot = new WhatsAppOutboundEngagementScopeSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = batchSnapshot.Id,
            WhatsAppConversationEngagementScopeId = scope.Id,
            EngagementId = engagement.Id,
            ApprovedAuthorizationVersion = scope.ApprovedAuthorizationVersion
        };
        db.AddRange(requestSnapshot, itemSnapshot, participantSnapshot, scopeSnapshot);
        await db.SaveChangesAsync();

        var logicalMessageKey = $"logical-message-{prefix}-{Guid.NewGuid():N}";
        var message = includeMessage
            ? new WhatsAppOutboundMessage
            {
                DocumentRequestBatchId = batch.Id,
                WhatsAppOutboundBatchSnapshotId = batchSnapshot.Id,
                LogicalMessageKey = logicalMessageKey,
                CorrelationId = $"message-correlation-{Guid.NewGuid():N}",
                ProviderName = conversation.ProviderName,
                BusinessEndpointKey = conversation.BusinessEndpointKey,
                ProviderAccountReference = conversation.ProviderAccountReference,
                DestinationKind = WhatsAppOutboundDestinationKind.Group,
                ProviderDestinationKey = $"group-destination-{Guid.NewGuid():N}",
                ContentKind = contentKind,
                TextBody = contentKind == WhatsAppOutboundContentKind.Text ? "Please provide the requested document." : null,
                TemplateName = contentKind == WhatsAppOutboundContentKind.Template ? "document_request" : null,
                TemplateLanguage = contentKind == WhatsAppOutboundContentKind.Template ? "en" : null,
                TemplateParametersSnapshot = contentKind == WhatsAppOutboundContentKind.Template ? "{\"request\":\"document\"}" : null
            }
            : null;
        if (message is not null)
        {
            db.WhatsAppOutboundMessages.Add(message);
            await db.SaveChangesAsync();
        }

        return new(
            contact.Id,
            address.Id,
            conversation.Id,
            participant.Id,
            engagement.Id,
            engagement.CustomerId,
            engagement.ServiceId,
            engagement.BusinessPartyId,
            scope.Id,
            batch.Id,
            batchSnapshot.Id,
            request.Id,
            requestItem.Id,
            message?.Id ?? 0,
            logicalMessageKey);
    }

    [PostgresFact]
    public async Task Phase10AMigrationCreatesOutboundSchemaAndDraftBatchDefault()
    {
        await using var db = await Fresh();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("_Phase10AOutboundPersistence", StringComparison.Ordinal));

        var tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('DocumentRequestBatchStatusHistories', 'WhatsAppOutboundBatchSnapshots', 'WhatsAppOutboundRequestSnapshots', 'WhatsAppOutboundItemSnapshots', 'WhatsAppOutboundParticipantSnapshots', 'WhatsAppOutboundEngagementScopeSnapshots', 'WhatsAppOutboundMessages', 'WhatsAppOutboundMessageAttempts')
            """).ToListAsync();
        Assert.Equal(8, tables.Count);

        var statusColumn = await db.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'DocumentRequestBatches' AND column_name = 'Status'
            """).ToListAsync();
        Assert.Single(statusColumn);

        var batch = new DocumentRequestBatch();
        db.DocumentRequestBatches.Add(batch);
        await db.SaveChangesAsync();
        Assert.Equal(DocumentRequestBatchStatus.Draft, batch.Status);
    }

    [PostgresFact]
    public async Task OutboundModelHasRestrictiveForeignKeysAndReferencedRowsCannotBeDeleted()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "restrictive-fks");

        var entityTypes = new[]
        {
            typeof(DocumentRequestBatchStatusHistory),
            typeof(WhatsAppOutboundBatchSnapshot),
            typeof(WhatsAppOutboundRequestSnapshot),
            typeof(WhatsAppOutboundItemSnapshot),
            typeof(WhatsAppOutboundParticipantSnapshot),
            typeof(WhatsAppOutboundEngagementScopeSnapshot),
            typeof(WhatsAppOutboundMessage),
            typeof(WhatsAppOutboundMessageAttempt)
        };
        Assert.All(entityTypes.SelectMany(type => db.Model.FindEntityType(type)!.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        await using var directDelete = Db();
        await AssertDocumentPersistenceFailure(() => directDelete.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"DocumentRequestBatches\" WHERE \"Id\" = {fixture.BatchId}"));
    }

    [PostgresFact]
    public async Task OutboundBatchSnapshotRequiresPositiveAuthorizationVersionAndSha256Hash()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "batch-checks");

        await using var invalidVersion = Db();
        await AssertDocumentPersistenceFailure(() => invalidVersion.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundBatchSnapshots\" SET \"ConversationAuthorizationVersion\" = 0 WHERE \"Id\" = {fixture.BatchSnapshotId}"));

        await using var invalidHash = Db();
        await AssertDocumentPersistenceFailure(() => invalidHash.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundBatchSnapshots\" SET \"ParticipantSetHash\" = {"not-a-sha256-hash"} WHERE \"Id\" = {fixture.BatchSnapshotId}"));
    }

    [PostgresFact]
    public async Task OneOutboundBatchSnapshotIsAllowedPerDocumentRequestBatch()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "one-batch-snapshot");
        var duplicate = new WhatsAppOutboundBatchSnapshot
        {
            DocumentRequestBatchId = fixture.BatchId,
            ContactId = fixture.ContactId,
            WhatsAppConversationId = fixture.ConversationId,
            ConversationAuthorizationVersion = 1,
            ParticipantSetHash = new string('b', 64),
            QueuedAt = ConversationTestTime,
            QueuedByActor = "phase10a-test",
            CorrelationId = "duplicate-batch-snapshot"
        };
        await using var duplicateContext = Db();
        duplicateContext.WhatsAppOutboundBatchSnapshots.Add(duplicate);
        await AssertDocumentPersistenceFailure(() => duplicateContext.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundRequestSnapshotRequiresPositiveRevisionAndVersion()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "request-checks");

        await using var invalidRevision = Db();
        await AssertDocumentPersistenceFailure(() => invalidRevision.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundRequestSnapshots\" SET \"RequestRevision\" = 0 WHERE \"DocumentRequestId\" = {fixture.RequestId}"));

        await using var invalidVersion = Db();
        await AssertDocumentPersistenceFailure(() => invalidVersion.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundRequestSnapshots\" SET \"RequestVersion\" = 0 WHERE \"DocumentRequestId\" = {fixture.RequestId}"));
    }

    [PostgresFact]
    public async Task OutboundRequestSnapshotIsUniquePerBatchAndRequest()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "request-unique");
        var duplicate = new WhatsAppOutboundRequestSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            DocumentRequestId = fixture.RequestId,
            RequestRevision = 1,
            RequestVersion = 1,
            EngagementId = fixture.EngagementId,
            CustomerId = fixture.CustomerId,
            CustomerNameSnapshot = "Duplicate customer",
            ServiceId = fixture.ServiceId,
            ServiceNameSnapshot = "Duplicate service"
        };
        db.WhatsAppOutboundRequestSnapshots.Add(duplicate);
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundItemSnapshotRequiresPositiveRevisionOrderAndKnownStatus()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "item-checks");

        await using var invalidRevision = Db();
        await AssertDocumentPersistenceFailure(() => invalidRevision.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundItemSnapshots\" SET \"RequestRevision\" = 0 WHERE \"DocumentRequestItemId\" = {fixture.ItemId}"));

        await using var invalidOrder = Db();
        await AssertDocumentPersistenceFailure(() => invalidOrder.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundItemSnapshots\" SET \"DisplayOrder\" = -1 WHERE \"DocumentRequestItemId\" = {fixture.ItemId}"));

        await using var invalidStatus = Db();
        await AssertDocumentPersistenceFailure(() => invalidStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundItemSnapshots\" SET \"ItemStatusSnapshot\" = 99 WHERE \"DocumentRequestItemId\" = {fixture.ItemId}"));
    }

    [PostgresFact]
    public async Task OutboundItemSnapshotCannotCrossDocumentRequestOwnershipBoundary()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "item-ownership");

        var itemForeignKey = db.Model.FindEntityType(typeof(WhatsAppOutboundItemSnapshot))!
            .GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(DocumentRequestItem));
        Assert.Equal(DeleteBehavior.Restrict, itemForeignKey.DeleteBehavior);
        Assert.Equal(
            new[] { "DocumentRequestId", "DocumentRequestItemId" },
            itemForeignKey.Properties.Select(x => x.Name).ToArray());
        Assert.Equal(
            new[] { "DocumentRequestId", "Id" },
            itemForeignKey.PrincipalKey.Properties.Select(x => x.Name).ToArray());

        var template = await AddDocumentTemplateAsync(db, fixture.ServiceId, DocumentToken("outbound-foreign-template"), 1);
        var templateItem = await AddDocumentTemplateItemAsync(db, template.Id, DocumentToken("outbound-foreign-requirement"));
        var documentFixture = await AddDocumentFixtureAsync(db, "outbound-foreign-request", fixture.ServiceId);
        var requestForeign = await AddDocumentRequestAsync(db, documentFixture.WorkItemId, template.Id);
        var itemForeign = await AddDocumentRequestItemAsync(db, requestForeign.Id, templateItem.Id, DocumentToken("outbound-foreign-item"));

        var matching = await db.WhatsAppOutboundItemSnapshots.AsNoTracking()
            .SingleAsync(x => x.WhatsAppOutboundBatchSnapshotId == fixture.BatchSnapshotId);
        Assert.Equal(fixture.RequestId, matching.DocumentRequestId);
        Assert.Equal(fixture.ItemId, matching.DocumentRequestItemId);

        await using var invalid = Db();
        invalid.WhatsAppOutboundItemSnapshots.Add(new WhatsAppOutboundItemSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            DocumentRequestId = fixture.RequestId,
            DocumentRequestItemId = itemForeign.Id,
            RequestRevision = 1,
            RequirementNameSnapshot = "Cross-request item",
            IsRequired = true,
            DisplayOrder = 2,
            ItemStatusSnapshot = DocumentRequestItemStatus.Missing
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        Assert.IsType<PostgresException>(exception.InnerException);
    }

    [PostgresFact]
    public async Task OutboundItemSnapshotIsUniquePerBatchAndItem()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "item-unique");
        var duplicate = new WhatsAppOutboundItemSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            DocumentRequestId = fixture.RequestId,
            DocumentRequestItemId = fixture.ItemId,
            RequestRevision = 1,
            RequirementNameSnapshot = "Duplicate requirement",
            IsRequired = true,
            DisplayOrder = 0,
            ItemStatusSnapshot = DocumentRequestItemStatus.Missing
        };
        db.WhatsAppOutboundItemSnapshots.Add(duplicate);
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundParticipantSnapshotPreservesTypedIdentityAndIsUnique()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "participant-typed");
        var participant = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = fixture.ConversationId,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = fixture.ContactId,
            ContactWhatsAppAddressId = fixture.AddressId,
            NormalizedE164 = "+60139000002",
            DisplayNameSnapshot = "Typed contact",
            JoinedAt = ConversationTestTime
        };
        db.WhatsAppConversationParticipants.Add(participant);
        await db.SaveChangesAsync();

        var snapshot = new WhatsAppOutboundParticipantSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            WhatsAppConversationParticipantId = participant.Id,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = fixture.ContactId,
            ContactWhatsAppAddressId = fixture.AddressId,
            NormalizedE164 = participant.NormalizedE164,
            DisplayNameSnapshot = participant.DisplayNameSnapshot
        };
        db.WhatsAppOutboundParticipantSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        Assert.Equal(fixture.ContactId, snapshot.ContactId);
        Assert.Equal(fixture.AddressId, snapshot.ContactWhatsAppAddressId);

        db.WhatsAppOutboundParticipantSnapshots.Add(new WhatsAppOutboundParticipantSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            WhatsAppConversationParticipantId = participant.Id,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = fixture.ContactId,
            ContactWhatsAppAddressId = fixture.AddressId,
            DisplayNameSnapshot = "Duplicate typed contact"
        });
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundParticipantSnapshotRejectsContradictoryTypedIdentity()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "participant-identity-check");
        var participant = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = fixture.ConversationId,
            ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
            JoinedAt = ConversationTestTime
        };
        db.WhatsAppConversationParticipants.Add(participant);
        await db.SaveChangesAsync();

        db.WhatsAppOutboundParticipantSnapshots.Add(new WhatsAppOutboundParticipantSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            WhatsAppConversationParticipantId = participant.Id,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = fixture.ContactId,
            BusinessPartyId = fixture.BusinessPartyId
        });
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundScopeSnapshotRequiresPositiveVersionAndUniqueMembership()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "scope-checks");

        await using var invalidVersion = Db();
        await AssertDocumentPersistenceFailure(() => invalidVersion.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundEngagementScopeSnapshots\" SET \"ApprovedAuthorizationVersion\" = 0 WHERE \"WhatsAppConversationEngagementScopeId\" = {fixture.ScopeId}"));

        var duplicate = new WhatsAppOutboundEngagementScopeSnapshot
        {
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            WhatsAppConversationEngagementScopeId = fixture.ScopeId,
            EngagementId = fixture.EngagementId,
            ApprovedAuthorizationVersion = 1
        };
        db.WhatsAppOutboundEngagementScopeSnapshots.Add(duplicate);
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundMessageLogicalKeyIsUniqueAcrossBatches()
    {
        await using var db = await Fresh();
        var first = await AddOutboundFixtureAsync(db, "message-key-first");
        var second = await AddOutboundFixtureAsync(db, "message-key-second");
        var duplicate = new WhatsAppOutboundMessage
        {
            DocumentRequestBatchId = second.BatchId,
            WhatsAppOutboundBatchSnapshotId = second.BatchSnapshotId,
            LogicalMessageKey = first.LogicalMessageKey,
            CorrelationId = "duplicate-logical-key-correlation",
            ProviderName = "Meta",
            BusinessEndpointKey = "duplicate-endpoint",
            DestinationKind = WhatsAppOutboundDestinationKind.Group,
            ProviderDestinationKey = "duplicate-group",
            ContentKind = WhatsAppOutboundContentKind.Text,
            TextBody = "Duplicate logical key"
        };
        await using var duplicateContext = Db();
        duplicateContext.WhatsAppOutboundMessages.Add(duplicate);
        await AssertDocumentPersistenceFailure(() => duplicateContext.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OnlyOneOutboundMessageCanReferenceABatchAndQueuedSnapshot()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "message-one-to-one");
        await using var duplicateContext = Db();
        duplicateContext.WhatsAppOutboundMessages.Add(new WhatsAppOutboundMessage
        {
            DocumentRequestBatchId = fixture.BatchId,
            WhatsAppOutboundBatchSnapshotId = fixture.BatchSnapshotId,
            LogicalMessageKey = "second-message-for-batch",
            CorrelationId = "second-message-correlation",
            ProviderName = "Meta",
            BusinessEndpointKey = "endpoint",
            DestinationKind = WhatsAppOutboundDestinationKind.Group,
            ProviderDestinationKey = "group",
            ContentKind = WhatsAppOutboundContentKind.Text,
            TextBody = "Second message"
        });
        await AssertDocumentPersistenceFailure(() => duplicateContext.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundTextMessageRequiresTextOnlyContentShape()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "text-shape");

        await using var missingText = Db();
        await AssertDocumentPersistenceFailure(() => missingText.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"TextBody\" = NULL WHERE \"Id\" = {fixture.MessageId}"));

        await using var mixedTemplate = Db();
        await AssertDocumentPersistenceFailure(() => mixedTemplate.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"TemplateName\" = {"template-name"} WHERE \"Id\" = {fixture.MessageId}"));
    }

    [PostgresFact]
    public async Task OutboundTemplateMessageRequiresTemplateNameAndLanguage()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "template-shape", contentKind: WhatsAppOutboundContentKind.Template);

        await using var missingName = Db();
        await AssertDocumentPersistenceFailure(() => missingName.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"TemplateName\" = NULL WHERE \"Id\" = {fixture.MessageId}"));

        await using var mixedText = Db();
        await AssertDocumentPersistenceFailure(() => mixedText.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"TextBody\" = {"text must not be present"} WHERE \"Id\" = {fixture.MessageId}"));
    }

    [PostgresFact]
    public async Task OutboundGroupMessageCannotCarryDirectRecipientFacts()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "group-recipient-facts");

        await using var invalidE164 = Db();
        await AssertDocumentPersistenceFailure(() => invalidE164.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"NormalizedE164\" = {"+60139000009"} WHERE \"Id\" = {fixture.MessageId}"));

        await using var invalidRecipient = Db();
        await AssertDocumentPersistenceFailure(() => invalidRecipient.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"ProviderRecipientKey\" = {"recipient-key"} WHERE \"Id\" = {fixture.MessageId}"));
    }

    [PostgresFact]
    public async Task OutboundMessageRequiresNonblankRoutingAndNonnegativeAttemptCount()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "message-reference-checks");

        await using var blankDestination = Db();
        await AssertDocumentPersistenceFailure(() => blankDestination.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"ProviderDestinationKey\" = {" "} WHERE \"Id\" = {fixture.MessageId}"));

        await using var negativeAttempts = Db();
        await AssertDocumentPersistenceFailure(() => negativeAttempts.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessages\" SET \"AttemptCount\" = -1 WHERE \"Id\" = {fixture.MessageId}"));
    }

    [PostgresFact]
    public async Task OutboundAttemptRequiresPositiveUniqueAttemptNumber()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "attempt-checks");
        var attempt = new WhatsAppOutboundMessageAttempt
        {
            WhatsAppOutboundMessageId = fixture.MessageId,
            AttemptNumber = 1,
            StartedAt = ConversationTestTime,
            CorrelationId = "attempt-correlation",
            Actor = "phase10a-test",
            Source = "tests"
        };
        db.WhatsAppOutboundMessageAttempts.Add(attempt);
        await db.SaveChangesAsync();

        db.WhatsAppOutboundMessageAttempts.Add(new WhatsAppOutboundMessageAttempt
        {
            WhatsAppOutboundMessageId = fixture.MessageId,
            AttemptNumber = 1,
            StartedAt = ConversationTestTime.AddMinutes(1),
            CorrelationId = "duplicate-attempt-correlation",
            Actor = "phase10a-test",
            Source = "tests"
        });
        await AssertDocumentPersistenceFailure(() => db.SaveChangesAsync());

        await using var invalidNumber = Db();
        await AssertDocumentPersistenceFailure(() => invalidNumber.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"WhatsAppOutboundMessageAttempts\" SET \"AttemptNumber\" = 0 WHERE \"Id\" = {attempt.Id}"));
    }

    [PostgresFact]
    public async Task OutboundSnapshotsAreImmutableAndCannotBeDeleted()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "snapshot-immutability");

        async Task AssertEditFailsAsync<T>(Func<AppDbContext, IQueryable<T>> query, Action<T> mutate)
            where T : DomainRecord
        {
            await using var context = Db();
            var row = await query(context).SingleAsync();
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertEditFailsAsync(c => c.WhatsAppOutboundBatchSnapshots.Where(x => x.Id == fixture.BatchSnapshotId), x => x.ParticipantSetHash = new string('b', 64));
        await AssertEditFailsAsync(c => c.WhatsAppOutboundRequestSnapshots.Where(x => x.DocumentRequestId == fixture.RequestId), x => x.CustomerNameSnapshot = "Rewritten customer");
        await AssertEditFailsAsync(c => c.WhatsAppOutboundItemSnapshots.Where(x => x.DocumentRequestItemId == fixture.ItemId), x => x.RequirementNameSnapshot = "Rewritten requirement");
        await AssertEditFailsAsync(c => c.WhatsAppOutboundParticipantSnapshots.Where(x => x.WhatsAppConversationParticipantId == fixture.ParticipantId), x => x.ProviderParticipantKey = "rewritten-participant");
        await AssertEditFailsAsync(c => c.WhatsAppOutboundEngagementScopeSnapshots.Where(x => x.WhatsAppConversationEngagementScopeId == fixture.ScopeId), x => x.ApprovedAuthorizationVersion = 2);

        await using var deleteContext = Db();
        var snapshot = await deleteContext.WhatsAppOutboundBatchSnapshots.SingleAsync(x => x.Id == fixture.BatchSnapshotId);
        deleteContext.Remove(snapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => deleteContext.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task OutboundMessageRoutingAndContentAreImmutable()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "message-immutability");

        async Task AssertEditFailsAsync(Action<WhatsAppOutboundMessage> mutate)
        {
            await using var context = Db();
            var row = await context.WhatsAppOutboundMessages.SingleAsync(x => x.Id == fixture.MessageId);
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertEditFailsAsync(x => x.ProviderName = "OtherProvider");
        await AssertEditFailsAsync(x => x.BusinessEndpointKey = "other-endpoint");
        await AssertEditFailsAsync(x => x.DestinationKind = WhatsAppOutboundDestinationKind.Direct);
        await AssertEditFailsAsync(x => x.ProviderDestinationKey = "other-destination");
        await AssertEditFailsAsync(x => x.TextBody = "rewritten body");
        await AssertEditFailsAsync(x => x.LogicalMessageKey = "rewritten-key");
    }

    [PostgresFact]
    public async Task OutboundMessageApprovedOperationalFieldsRemainMutable()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "message-operations");
        var nextAttempt = ConversationTestTime.AddHours(1);
        var providerTimestamp = ConversationTestTime.AddMinutes(2);

        await using (var context = Db())
        {
            var message = await context.WhatsAppOutboundMessages.SingleAsync(x => x.Id == fixture.MessageId);
            message.State = WhatsAppOutboundMessageState.Accepted;
            message.AttemptCount = 1;
            message.NextAttemptAt = nextAttempt;
            message.ProviderMessageId = "provider-message-1";
            message.LastErrorCategory = "temporary";
            message.LastErrorCode = "retryable";
            message.ProviderRetryAfterUntil = nextAttempt;
            message.ProviderTimestamp = providerTimestamp;
            await context.SaveChangesAsync();
        }

        await using var verify = Db();
        var updated = await verify.WhatsAppOutboundMessages.SingleAsync(x => x.Id == fixture.MessageId);
        Assert.Equal(WhatsAppOutboundMessageState.Accepted, updated.State);
        Assert.Equal(1, updated.AttemptCount);
        Assert.Equal(nextAttempt, updated.NextAttemptAt);
        Assert.Equal("provider-message-1", updated.ProviderMessageId);
        Assert.Equal("temporary", updated.LastErrorCategory);
        Assert.Equal("retryable", updated.LastErrorCode);
        Assert.Equal(nextAttempt, updated.ProviderRetryAfterUntil);
        Assert.Equal(providerTimestamp, updated.ProviderTimestamp);
    }

    [PostgresFact]
    public async Task OutboundBatchHistoryAndAttemptsAreAppendOnly()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "append-only");
        var history = new DocumentRequestBatchStatusHistory
        {
            DocumentRequestBatchId = fixture.BatchId,
            PreviousStatus = DocumentRequestBatchStatus.Draft,
            NewStatus = DocumentRequestBatchStatus.Ready,
            Action = "Prepared",
            Reason = "Persistence test",
            Actor = "phase10a-test",
            Source = "tests",
            CorrelationId = "batch-history-correlation",
            OccurredAt = ConversationTestTime
        };
        var attempt = new WhatsAppOutboundMessageAttempt
        {
            WhatsAppOutboundMessageId = fixture.MessageId,
            AttemptNumber = 1,
            StartedAt = ConversationTestTime,
            CorrelationId = "append-attempt-correlation",
            Actor = "phase10a-test",
            Source = "tests"
        };
        db.AddRange(history, attempt);
        await db.SaveChangesAsync();

        await using (var editHistory = Db())
        {
            var row = await editHistory.DocumentRequestBatchStatusHistories.SingleAsync(x => x.Id == history.Id);
            row.Action = "Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => editHistory.SaveChangesAsync());
        }
        await using (var deleteHistory = Db())
        {
            var row = await deleteHistory.DocumentRequestBatchStatusHistories.SingleAsync(x => x.Id == history.Id);
            deleteHistory.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteHistory.SaveChangesAsync());
        }
        await using (var editAttempt = Db())
        {
            var row = await editAttempt.WhatsAppOutboundMessageAttempts.SingleAsync(x => x.Id == attempt.Id);
            row.Disposition = "Tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => editAttempt.SaveChangesAsync());
        }
        await using (var deleteAttempt = Db())
        {
            var row = await deleteAttempt.WhatsAppOutboundMessageAttempts.SingleAsync(x => x.Id == attempt.Id);
            deleteAttempt.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteAttempt.SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task DocumentRequestBatchStatusSupportsOnlyTheControlledStatusField()
    {
        await using var db = await Fresh();
        var batch = new DocumentRequestBatch();
        db.DocumentRequestBatches.Add(batch);
        await db.SaveChangesAsync();
        Assert.Equal(DocumentRequestBatchStatus.Draft, batch.Status);

        batch.Status = DocumentRequestBatchStatus.Ready;
        await db.SaveChangesAsync();
        Assert.Equal(DocumentRequestBatchStatus.Ready, batch.Status);

        await using var invalidStatus = Db();
        await AssertDocumentPersistenceFailure(() => invalidStatus.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"DocumentRequestBatches\" SET \"Status\" = 99 WHERE \"Id\" = {batch.Id}"));
    }

    [PostgresFact]
    public async Task OutboundPersistenceDoesNotMutateRequestsItemsWorkOrFinancialRecords()
    {
        await using var db = await Fresh();
        var fixture = await AddOutboundFixtureAsync(db, "boundary");
        var beforeRequest = await db.DocumentRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.RequestId);
        var beforeItem = await db.DocumentRequestItems.AsNoTracking().SingleAsync(x => x.Id == fixture.ItemId);
        var beforeWorkItem = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == beforeRequest.WorkItemId);
        var beforeBilling = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.WorkItem.Id == beforeWorkItem.Id);
        var beforeCounts = (
            Requests: await db.DocumentRequests.CountAsync(),
            Items: await db.DocumentRequestItems.CountAsync(),
            WorkItems: await db.WorkItems.CountAsync(),
            BillingRecords: await db.BillingRecords.CountAsync(),
            Invoices: await db.Invoices.CountAsync(),
            Payments: await db.WorkerPayments.CountAsync(),
            Receipts: await db.CustomerReceipts.CountAsync());

        db.Add(new WhatsAppOutboundMessageAttempt
        {
            WhatsAppOutboundMessageId = fixture.MessageId,
            AttemptNumber = 1,
            StartedAt = ConversationTestTime,
            CorrelationId = "boundary-attempt",
            Actor = "phase10a-test",
            Source = "tests"
        });
        db.Add(new DocumentRequestBatchStatusHistory
        {
            DocumentRequestBatchId = fixture.BatchId,
            PreviousStatus = DocumentRequestBatchStatus.Draft,
            NewStatus = DocumentRequestBatchStatus.Ready,
            Action = "Prepared",
            Actor = "phase10a-test",
            Source = "tests",
            OccurredAt = ConversationTestTime
        });
        await db.SaveChangesAsync();

        var afterRequest = await db.DocumentRequests.AsNoTracking().SingleAsync(x => x.Id == fixture.RequestId);
        var afterItem = await db.DocumentRequestItems.AsNoTracking().SingleAsync(x => x.Id == fixture.ItemId);
        var afterWorkItem = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == beforeWorkItem.Id);
        var afterBilling = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == beforeBilling.Id);
        Assert.Equal(beforeRequest.Status, afterRequest.Status);
        Assert.Equal(beforeRequest.Version, afterRequest.Version);
        Assert.Equal(beforeItem.Status, afterItem.Status);
        Assert.Equal(beforeItem.Version, afterItem.Version);
        Assert.Equal(beforeWorkItem.Status, afterWorkItem.Status);
        Assert.Equal(beforeWorkItem.Version, afterWorkItem.Version);
        Assert.Equal(beforeBilling.Status, afterBilling.Status);
        Assert.Equal(beforeBilling.Version, afterBilling.Version);
        Assert.Equal(beforeCounts, (
            Requests: await db.DocumentRequests.CountAsync(),
            Items: await db.DocumentRequestItems.CountAsync(),
            WorkItems: await db.WorkItems.CountAsync(),
            BillingRecords: await db.BillingRecords.CountAsync(),
            Invoices: await db.Invoices.CountAsync(),
            Payments: await db.WorkerPayments.CountAsync(),
            Receipts: await db.CustomerReceipts.CountAsync()));
    }
}
