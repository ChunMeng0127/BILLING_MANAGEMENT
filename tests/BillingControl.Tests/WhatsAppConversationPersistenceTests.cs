using BillingControl.Data;
using BillingControl.Models;
using BillingControl.Services;
using Microsoft.EntityFrameworkCore;
using DomainRecord = BillingControl.Models.Record;

namespace BillingControl.Tests;

public partial class IntegrationTests
{
    private static DateTime ConversationTestTime => new(2026, 9, 13, 4, 0, 0, DateTimeKind.Utc);

    private static async Task<WhatsAppConversation> AddGroupConversationAsync(
        AppDbContext db,
        string prefix,
        string businessEndpointKey = "business-endpoint-1",
        int authorizationVersion = 1,
        WhatsAppConversationStatus status = WhatsAppConversationStatus.Active)
    {
        var conversation = new WhatsAppConversation
        {
            Kind = WhatsAppConversationKind.Group,
            Status = status,
            ProviderName = "Meta",
            BusinessEndpointKey = businessEndpointKey,
            ProviderAccountReference = $"account-{prefix}",
            ProviderConversationKey = ConversationToken(prefix),
            AuthorizationVersion = authorizationVersion
        };
        db.WhatsAppConversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static async Task<WhatsAppConversation> AddDirectConversationAsync(
        AppDbContext db,
        string prefix,
        int addressId,
        string businessEndpointKey = "business-endpoint-1",
        int authorizationVersion = 1,
        WhatsAppConversationStatus status = WhatsAppConversationStatus.Active)
    {
        var conversation = new WhatsAppConversation
        {
            Kind = WhatsAppConversationKind.Direct,
            Status = status,
            ProviderName = "Meta",
            BusinessEndpointKey = businessEndpointKey,
            ProviderAccountReference = $"account-{prefix}",
            ProviderConversationKey = ConversationToken(prefix),
            DirectContactWhatsAppAddressId = addressId,
            AuthorizationVersion = authorizationVersion
        };
        db.WhatsAppConversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    private static async Task<AppUser> AddConversationAppUserAsync(AppDbContext db, string prefix)
    {
        var userName = $"conversation-{prefix}-{Guid.NewGuid():N}@example.com";
        var user = new AppUser
        {
            Id = $"conversation-user-{Guid.NewGuid():N}",
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static string ConversationToken(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [PostgresFact]
    public async Task Phase9AMigrationAppliesAndConversationModelHasRestrictiveRelationships()
    {
        await using var db = await Fresh();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("_Phase9AWhatsAppConversationPersistence", StringComparison.Ordinal));

        var tables = await db.Database.SqlQuery<string>($"""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN ('WhatsAppConversations', 'WhatsAppConversationParticipants', 'WhatsAppConversationEngagementScopes', 'WhatsAppConversationHistories', 'WhatsAppConversationParticipantHistories', 'WhatsAppConversationEngagementScopeHistories')
            """).ToListAsync();
        Assert.Equal(6, tables.Count);

        var entityTypes = new[]
        {
            typeof(WhatsAppConversation),
            typeof(WhatsAppConversationParticipant),
            typeof(WhatsAppConversationEngagementScope),
            typeof(WhatsAppConversationHistory),
            typeof(WhatsAppConversationParticipantHistory),
            typeof(WhatsAppConversationEngagementScopeHistory)
        };
        Assert.All(entityTypes.SelectMany(type => db.Model.FindEntityType(type)!.GetForeignKeys()),
            foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));

        Assert.DoesNotContain(
            db.Model.FindEntityType(typeof(WhatsAppConversationEngagementScope))!.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ContactCustomerLink));
    }

    [PostgresFact]
    public async Task ConversationKindAddressAndAuthorizationVersionConstraintsAreEnforced()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "phase9a-direct-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139000001");

        await AddDirectConversationAsync(db, "valid-direct", address.Id);
        await AddGroupConversationAsync(db, "valid-group");

        async Task AssertConversationFailsAsync(Action<WhatsAppConversation> configure)
        {
            await using var invalid = Db();
            var candidate = new WhatsAppConversation
            {
                Kind = WhatsAppConversationKind.Group,
                ProviderName = "Meta",
                BusinessEndpointKey = "phase9a-invalid-endpoint",
                ProviderConversationKey = ConversationToken("invalid"),
                AuthorizationVersion = 1
            };
            configure(candidate);
            invalid.WhatsAppConversations.Add(candidate);
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }

        await AssertConversationFailsAsync(candidate =>
        {
            candidate.Kind = WhatsAppConversationKind.Direct;
            candidate.DirectContactWhatsAppAddressId = null;
        });
        await AssertConversationFailsAsync(candidate =>
        {
            candidate.Kind = WhatsAppConversationKind.Group;
            candidate.DirectContactWhatsAppAddressId = address.Id;
        });
        await AssertConversationFailsAsync(candidate => candidate.AuthorizationVersion = 0);
        await AssertConversationFailsAsync(candidate => candidate.ProviderName = " ");
        await AssertConversationFailsAsync(candidate => candidate.BusinessEndpointKey = " ");
        await AssertConversationFailsAsync(candidate => candidate.ProviderConversationKey = " ");
        await AssertConversationFailsAsync(candidate => candidate.ProviderAccountReference = " ");
    }

    [PostgresFact]
    public async Task ConversationProviderIdentityAndNonInactiveDirectDestinationAreUnique()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "phase9a-identity-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139000002");

        await AddGroupConversationAsync(db, "identity-one", "identity-endpoint");
        await using (var duplicateIdentity = Db())
        {
            duplicateIdentity.WhatsAppConversations.Add(new WhatsAppConversation
            {
                Kind = WhatsAppConversationKind.Group,
                ProviderName = "Meta",
                BusinessEndpointKey = "identity-endpoint",
                ProviderConversationKey = (await db.WhatsAppConversations.AsNoTracking().Select(x => x.ProviderConversationKey).SingleAsync()),
                AuthorizationVersion = 1
            });
            await AssertDocumentPersistenceFailure(() => duplicateIdentity.SaveChangesAsync());
        }

        await AddDirectConversationAsync(db, "direct-identity-one", address.Id, "direct-endpoint");
        await AddDirectConversationAsync(db, "direct-inactive", address.Id, "direct-endpoint", status: WhatsAppConversationStatus.Inactive);

        await using (var duplicateDirect = Db())
        {
            duplicateDirect.WhatsAppConversations.Add(new WhatsAppConversation
            {
                Kind = WhatsAppConversationKind.Direct,
                ProviderName = "Meta",
                BusinessEndpointKey = "direct-endpoint",
                ProviderConversationKey = ConversationToken("direct-review"),
                DirectContactWhatsAppAddressId = address.Id,
                Status = WhatsAppConversationStatus.NeedsAuthorizationReview,
                AuthorizationVersion = 1
            });
            await AssertDocumentPersistenceFailure(() => duplicateDirect.SaveChangesAsync());
        }

        var directForeignKey = db.Model.FindEntityType(typeof(WhatsAppConversation))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(WhatsAppConversation.DirectContactWhatsAppAddressId)));
        Assert.Equal(DeleteBehavior.Restrict, directForeignKey.DeleteBehavior);

        await using var directDelete = Db();
        await AssertDocumentPersistenceFailure(() => directDelete.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ContactWhatsAppAddresses\" WHERE \"Id\" = {address.Id}"));
    }

    [PostgresFact]
    public async Task ParticipantKindsRequireOnlyTheirStronglyTypedIdentityMappings()
    {
        await using var db = await Fresh();
        var conversation = await AddGroupConversationAsync(db, "participant-kinds");
        var contact = await AddContactAsync(db, "participant-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139000003");
        var otherContact = await AddContactAsync(db, "participant-other-contact");
        var businessParty = new BusinessParty { Name = ContactToken("participant-business") };
        var manager = new Manager { Name = ContactToken("participant-manager") };
        db.AddRange(businessParty, manager);
        await db.SaveChangesAsync();
        var appUser = await AddConversationAppUserAsync(db, "participant");
        var joined = ConversationTestTime;

        db.WhatsAppConversationParticipants.AddRange(
            new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.Contact,
                ContactId = contact.Id,
                ContactWhatsAppAddressId = address.Id,
                ProviderParticipantKey = "provider-contact-valid",
                JoinedAt = joined
            },
            new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.BusinessParty,
                BusinessPartyId = businessParty.Id,
                ProviderParticipantKey = "provider-business-valid",
                JoinedAt = joined
            },
            new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.Manager,
                ManagerId = manager.Id,
                ProviderParticipantKey = "provider-manager-valid",
                JoinedAt = joined
            },
            new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.AppUser,
                AppUserId = appUser.Id,
                ProviderParticipantKey = "provider-app-user-valid",
                JoinedAt = joined
            },
            new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.BusinessSender,
                ProviderParticipantKey = "provider-sender-valid",
                JoinedAt = joined
            },
            new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
                ProviderParticipantKey = "provider-external-valid",
                JoinedAt = joined
            });
        await db.SaveChangesAsync();

        async Task AssertParticipantFailsAsync(Action<WhatsAppConversationParticipant> configure)
        {
            await using var invalid = Db();
            var candidate = new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
                JoinedAt = joined
            };
            configure(candidate);
            invalid.WhatsAppConversationParticipants.Add(candidate);
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }

        await AssertParticipantFailsAsync(candidate => candidate.ParticipantKind = WhatsAppParticipantKind.Contact);
        await AssertParticipantFailsAsync(candidate => candidate.ParticipantKind = WhatsAppParticipantKind.BusinessParty);
        await AssertParticipantFailsAsync(candidate => candidate.ParticipantKind = WhatsAppParticipantKind.Manager);
        await AssertParticipantFailsAsync(candidate => candidate.ParticipantKind = WhatsAppParticipantKind.AppUser);
        await AssertParticipantFailsAsync(candidate =>
        {
            candidate.ParticipantKind = WhatsAppParticipantKind.BusinessSender;
            candidate.ContactId = contact.Id;
        });
        await AssertParticipantFailsAsync(candidate =>
        {
            candidate.ParticipantKind = WhatsAppParticipantKind.Contact;
            candidate.ContactId = contact.Id;
            candidate.BusinessPartyId = businessParty.Id;
        });
        await AssertParticipantFailsAsync(candidate =>
        {
            candidate.ParticipantKind = WhatsAppParticipantKind.Contact;
            candidate.ContactId = otherContact.Id;
            candidate.ContactWhatsAppAddressId = int.MaxValue;
        });

        var addressForeignKey = db.Model.FindEntityType(typeof(WhatsAppConversationParticipant))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == nameof(WhatsAppConversationParticipant.ContactWhatsAppAddressId)));
        Assert.Equal(DeleteBehavior.Restrict, addressForeignKey.DeleteBehavior);
    }

    [PostgresFact]
    public async Task ContactParticipantAddressMustBelongToTheSameContact()
    {
        await using var db = await Fresh();
        var mismatchConversation = await AddGroupConversationAsync(db, "participant-contact-address-mismatch");
        var matchingConversation = await AddGroupConversationAsync(db, "participant-contact-address-match");
        var contactOnlyConversation = await AddGroupConversationAsync(db, "participant-contact-address-omitted");
        var contactA = await AddContactAsync(db, "participant-contact-a");
        var addressA = await AddContactAddressAsync(db, contactA.Id, "+60139000008");
        var contactB = await AddContactAsync(db, "participant-contact-b");
        var addressB = await AddContactAddressAsync(db, contactB.Id, "+60139000009");

        var ownershipForeignKey = Assert.Single(
            db.Model.FindEntityType(typeof(WhatsAppConversationParticipant))!.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ContactWhatsAppAddress));
        Assert.Equal(
            new[] { nameof(WhatsAppConversationParticipant.ContactId), nameof(WhatsAppConversationParticipant.ContactWhatsAppAddressId) },
            ownershipForeignKey.Properties.Select(property => property.Name));
        Assert.Equal(
            new[] { nameof(ContactWhatsAppAddress.ContactId), nameof(ContactWhatsAppAddress.Id) },
            ownershipForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, ownershipForeignKey.DeleteBehavior);

        await using (var mismatch = Db())
        {
            mismatch.WhatsAppConversationParticipants.Add(new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = mismatchConversation.Id,
                ParticipantKind = WhatsAppParticipantKind.Contact,
                ContactId = contactA.Id,
                ContactWhatsAppAddressId = addressB.Id,
                JoinedAt = ConversationTestTime
            });
            await AssertDocumentPersistenceFailure(() => mismatch.SaveChangesAsync());
        }

        await using (var matching = Db())
        {
            matching.WhatsAppConversationParticipants.Add(new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = matchingConversation.Id,
                ParticipantKind = WhatsAppParticipantKind.Contact,
                ContactId = contactA.Id,
                ContactWhatsAppAddressId = addressA.Id,
                JoinedAt = ConversationTestTime
            });
            await matching.SaveChangesAsync();
        }

        await using (var contactOnly = Db())
        {
            contactOnly.WhatsAppConversationParticipants.Add(new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = contactOnlyConversation.Id,
                ParticipantKind = WhatsAppParticipantKind.Contact,
                ContactId = contactB.Id,
                ContactWhatsAppAddressId = null,
                JoinedAt = ConversationTestTime
            });
            await contactOnly.SaveChangesAsync();
        }
    }

    [PostgresFact]
    public async Task ParticipantActiveMappingsAndProviderKeysHaveHistoricalDuplicateProtection()
    {
        await using var db = await Fresh();
        var conversation = await AddGroupConversationAsync(db, "participant-duplicates");
        var contact = await AddContactAsync(db, "participant-duplicate-contact");
        var joined = ConversationTestTime;

        var first = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = contact.Id,
            ProviderParticipantKey = "provider-duplicate-contact-one",
            JoinedAt = joined
        };
        db.WhatsAppConversationParticipants.Add(first);
        await db.SaveChangesAsync();

        await using (var duplicateMapped = Db())
        {
            duplicateMapped.WhatsAppConversationParticipants.Add(new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.Contact,
                ContactId = contact.Id,
                ProviderParticipantKey = "provider-duplicate-contact-two",
                JoinedAt = joined
            });
            await AssertDocumentPersistenceFailure(() => duplicateMapped.SaveChangesAsync());
        }

        var inactive = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = contact.Id,
            ProviderParticipantKey = "provider-duplicate-contact-inactive",
            IsActive = false,
            JoinedAt = joined,
            LeftAt = joined.AddHours(1)
        };
        db.WhatsAppConversationParticipants.Add(inactive);
        await db.SaveChangesAsync();

        var providerKeyOwner = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.BusinessSender,
            ProviderParticipantKey = "provider-duplicate-key",
            JoinedAt = joined
        };
        db.WhatsAppConversationParticipants.Add(providerKeyOwner);
        await db.SaveChangesAsync();

        await using (var duplicateProviderKey = Db())
        {
            duplicateProviderKey.WhatsAppConversationParticipants.Add(new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
                ProviderParticipantKey = "provider-duplicate-key",
                JoinedAt = joined
            });
            await AssertDocumentPersistenceFailure(() => duplicateProviderKey.SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task ParticipantLifecycleAndSnapshotConstraintsAreEnforced()
    {
        await using var db = await Fresh();
        var conversation = await AddGroupConversationAsync(db, "participant-lifecycle");
        var joined = ConversationTestTime;

        async Task AssertParticipantFailsAsync(Action<WhatsAppConversationParticipant> configure)
        {
            await using var invalid = Db();
            var candidate = new WhatsAppConversationParticipant
            {
                WhatsAppConversationId = conversation.Id,
                ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
                JoinedAt = joined
            };
            configure(candidate);
            invalid.WhatsAppConversationParticipants.Add(candidate);
            await AssertDocumentPersistenceFailure(() => invalid.SaveChangesAsync());
        }

        await AssertParticipantFailsAsync(candidate =>
        {
            candidate.IsActive = true;
            candidate.LeftAt = joined.AddHours(1);
        });
        await AssertParticipantFailsAsync(candidate =>
        {
            candidate.IsActive = false;
            candidate.LeftAt = null;
        });
        await AssertParticipantFailsAsync(candidate => candidate.LeftAt = joined.AddHours(-1));
        await AssertParticipantFailsAsync(candidate => candidate.ProviderParticipantKey = " ");
        await AssertParticipantFailsAsync(candidate => candidate.NormalizedE164 = "60139000004");
        await AssertParticipantFailsAsync(candidate => candidate.DisplayNameSnapshot = " ");

        await using var validInactive = Db();
        var inactive = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
            IsActive = false,
            JoinedAt = joined,
            LeftAt = joined.AddHours(2),
            NormalizedE164 = "+60139000004",
            DisplayNameSnapshot = "Former participant"
        };
        validInactive.WhatsAppConversationParticipants.Add(inactive);
        await validInactive.SaveChangesAsync();
        Assert.False(inactive.IsActive);
    }

    [PostgresFact]
    public async Task EngagementScopeIsDurableRestrictiveUniqueAndVersionAwareWithoutInferringAuthorization()
    {
        await using var db = await Fresh();
        var conversation = await AddGroupConversationAsync(db, "scope");
        var engagementId = await Engagement(db);
        var approvedAt = ConversationTestTime;
        var scope = new WhatsAppConversationEngagementScope
        {
            WhatsAppConversationId = conversation.Id,
            EngagementId = engagementId,
            ApprovedAuthorizationVersion = 1,
            ApprovedAt = approvedAt,
            ApprovedByActor = "manager-1",
            ApprovalReason = "Reviewed participants"
        };
        db.WhatsAppConversationEngagementScopes.Add(scope);
        await db.SaveChangesAsync();

        await using (var duplicate = Db())
        {
            duplicate.WhatsAppConversationEngagementScopes.Add(new WhatsAppConversationEngagementScope
            {
                WhatsAppConversationId = conversation.Id,
                EngagementId = engagementId,
                ApprovedAuthorizationVersion = 1,
                ApprovedAt = approvedAt,
                ApprovedByActor = "manager-2"
            });
            await AssertDocumentPersistenceFailure(() => duplicate.SaveChangesAsync());
        }

        await using (var invalidVersion = Db())
        {
            invalidVersion.WhatsAppConversationEngagementScopes.Add(new WhatsAppConversationEngagementScope
            {
                WhatsAppConversationId = conversation.Id,
                EngagementId = await Engagement(invalidVersion),
                ApprovedAuthorizationVersion = 0,
                ApprovedAt = approvedAt,
                ApprovedByActor = "manager-3"
            });
            await AssertDocumentPersistenceFailure(() => invalidVersion.SaveChangesAsync());
        }

        conversation.AuthorizationVersion = 2;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var savedScope = await db.WhatsAppConversationEngagementScopes.AsNoTracking().SingleAsync(x => x.Id == scope.Id);
        var savedConversation = await db.WhatsAppConversations.AsNoTracking().SingleAsync(x => x.Id == conversation.Id);
        Assert.Equal(1, savedScope.ApprovedAuthorizationVersion);
        Assert.Equal(2, savedConversation.AuthorizationVersion);
        Assert.NotEqual(savedScope.ApprovedAuthorizationVersion, savedConversation.AuthorizationVersion);

        await using (var invalidRevocation = Db())
        {
            invalidRevocation.WhatsAppConversationEngagementScopes.Add(new WhatsAppConversationEngagementScope
            {
                WhatsAppConversationId = conversation.Id,
                EngagementId = await Engagement(invalidRevocation),
                ApprovedAuthorizationVersion = 2,
                ApprovedAt = approvedAt,
                ApprovedByActor = "manager-4",
                IsActive = false,
                RevokedAt = approvedAt
            });
            await AssertDocumentPersistenceFailure(() => invalidRevocation.SaveChangesAsync());
        }

        var scopeForeignKeys = db.Model.FindEntityType(typeof(WhatsAppConversationEngagementScope))!.GetForeignKeys();
        Assert.Contains(scopeForeignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Engagement) && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        await using var directDelete = Db();
        await AssertDocumentPersistenceFailure(() => directDelete.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Engagements\" WHERE \"Id\" = {engagementId}"));
    }

    [PostgresFact]
    public async Task ConversationParticipantAndScopeIdentityFieldsCannotBeRewritten()
    {
        await using var db = await Fresh();
        var contact = await AddContactAsync(db, "immutability-phase9a-contact");
        var otherContact = await AddContactAsync(db, "immutability-phase9a-other-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139000005");
        var otherAddress = await AddContactAddressAsync(db, otherContact.Id, "+60139000006");
        var conversation = await AddDirectConversationAsync(db, "immutability-phase9a-conversation", address.Id);
        var otherConversation = await AddGroupConversationAsync(db, "immutability-phase9a-other-conversation");
        var participant = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.Contact,
            ContactId = contact.Id,
            ContactWhatsAppAddressId = address.Id,
            ProviderParticipantKey = "immutability-provider-key",
            NormalizedE164 = "+60139000005",
            DisplayNameSnapshot = "Immutable contact",
            JoinedAt = ConversationTestTime
        };
        db.WhatsAppConversationParticipants.Add(participant);
        var engagementId = await Engagement(db);
        var scope = new WhatsAppConversationEngagementScope
        {
            WhatsAppConversationId = conversation.Id,
            EngagementId = engagementId,
            ApprovedAuthorizationVersion = 1,
            ApprovedAt = ConversationTestTime,
            ApprovedByActor = "manager"
        };
        db.WhatsAppConversationEngagementScopes.Add(scope);
        await db.SaveChangesAsync();

        async Task AssertConversationRewriteFailsAsync(Action<WhatsAppConversation> mutate)
        {
            await using var context = Db();
            var row = await context.WhatsAppConversations.SingleAsync(x => x.Id == conversation.Id);
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertConversationRewriteFailsAsync(row => row.Kind = WhatsAppConversationKind.Group);
        await AssertConversationRewriteFailsAsync(row => row.ProviderName = "OtherProvider");
        await AssertConversationRewriteFailsAsync(row => row.BusinessEndpointKey = "other-endpoint");
        await AssertConversationRewriteFailsAsync(row => row.ProviderAccountReference = "other-account");
        await AssertConversationRewriteFailsAsync(row => row.ProviderConversationKey = "other-conversation-key");
        await AssertConversationRewriteFailsAsync(row => row.DirectContactWhatsAppAddressId = otherAddress.Id);

        async Task AssertParticipantRewriteFailsAsync(Action<WhatsAppConversationParticipant> mutate)
        {
            await using var context = Db();
            var row = await context.WhatsAppConversationParticipants.SingleAsync(x => x.Id == participant.Id);
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertParticipantRewriteFailsAsync(row => row.WhatsAppConversationId = otherConversation.Id);
        await AssertParticipantRewriteFailsAsync(row => row.ParticipantKind = WhatsAppParticipantKind.BusinessSender);
        await AssertParticipantRewriteFailsAsync(row => row.ContactId = otherContact.Id);
        await AssertParticipantRewriteFailsAsync(row => row.ContactWhatsAppAddressId = otherAddress.Id);
        await AssertParticipantRewriteFailsAsync(row => row.ProviderParticipantKey = "rewritten-provider-key");
        await AssertParticipantRewriteFailsAsync(row => row.NormalizedE164 = "+60139000006");
        await AssertParticipantRewriteFailsAsync(row => row.DisplayNameSnapshot = "Rewritten contact");

        await using (var scopeRewrite = Db())
        {
            var row = await scopeRewrite.WhatsAppConversationEngagementScopes.SingleAsync(x => x.Id == scope.Id);
            row.WhatsAppConversationId = otherConversation.Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => scopeRewrite.SaveChangesAsync());
        }

        await using (var allowedLifecycle = Db())
        {
            var row = await allowedLifecycle.WhatsAppConversations.SingleAsync(x => x.Id == conversation.Id);
            row.Status = WhatsAppConversationStatus.NeedsAuthorizationReview;
            row.AuthorizationVersion = 2;
            await allowedLifecycle.SaveChangesAsync();
        }
    }

    [PostgresFact]
    public async Task Phase9AHistoryRowsCaptureFactsAndAreAppendOnly()
    {
        await using var db = await Fresh();
        var conversation = await AddGroupConversationAsync(db, "history-phase9a");
        var joined = ConversationTestTime;
        var participant = new WhatsAppConversationParticipant
        {
            WhatsAppConversationId = conversation.Id,
            ParticipantKind = WhatsAppParticipantKind.UnknownExternal,
            JoinedAt = joined
        };
        db.WhatsAppConversationParticipants.Add(participant);
        var engagementId = await Engagement(db);
        var scope = new WhatsAppConversationEngagementScope
        {
            WhatsAppConversationId = conversation.Id,
            EngagementId = engagementId,
            ApprovedAuthorizationVersion = 1,
            ApprovedAt = joined,
            ApprovedByActor = "manager",
            ApprovalReason = "Initial approval"
        };
        db.WhatsAppConversationEngagementScopes.Add(scope);
        await db.SaveChangesAsync();

        var conversationHistory = new WhatsAppConversationHistory
        {
            WhatsAppConversationId = conversation.Id,
            PreviousStatus = null,
            NewStatus = WhatsAppConversationStatus.Active,
            PreviousAuthorizationVersion = null,
            NewAuthorizationVersion = 1,
            Action = "Created",
            Actor = "tester",
            Source = "integration",
            OccurredAt = joined
        };
        var participantHistory = new WhatsAppConversationParticipantHistory
        {
            WhatsAppConversationParticipantId = participant.Id,
            PreviousIsActive = null,
            NewIsActive = true,
            PreviousJoinedAt = null,
            NewJoinedAt = joined,
            PreviousLeftAt = null,
            NewLeftAt = null,
            Action = "Joined",
            Actor = "tester",
            Source = "integration",
            OccurredAt = joined
        };
        var scopeHistory = new WhatsAppConversationEngagementScopeHistory
        {
            WhatsAppConversationEngagementScopeId = scope.Id,
            PreviousIsActive = null,
            NewIsActive = true,
            PreviousApprovedAuthorizationVersion = null,
            NewApprovedAuthorizationVersion = 1,
            PreviousApprovedAt = null,
            NewApprovedAt = joined,
            PreviousApprovedByActor = null,
            NewApprovedByActor = "manager",
            PreviousApprovalReason = null,
            NewApprovalReason = "Initial approval",
            PreviousRevokedAt = null,
            NewRevokedAt = null,
            PreviousRevokedByActor = null,
            NewRevokedByActor = null,
            PreviousRevocationReason = null,
            NewRevocationReason = null,
            Action = "Approved",
            Actor = "tester",
            Source = "integration",
            OccurredAt = joined
        };
        db.AddRange(conversationHistory, participantHistory, scopeHistory);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.WhatsAppConversationHistories.CountAsync(x => x.WhatsAppConversationId == conversation.Id));
        Assert.Equal(1, await db.WhatsAppConversationParticipantHistories.CountAsync(x => x.WhatsAppConversationParticipantId == participant.Id));
        Assert.Equal(1, await db.WhatsAppConversationEngagementScopeHistories.CountAsync(x => x.WhatsAppConversationEngagementScopeId == scope.Id));

        async Task AssertHistoryModificationFailsAsync<T>(Func<AppDbContext, IQueryable<T>> query, Action<T> mutate)
            where T : DomainRecord
        {
            await using var context = Db();
            var row = await query(context).SingleAsync();
            mutate(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await AssertHistoryModificationFailsAsync(c => c.WhatsAppConversationHistories.Where(x => x.Id == conversationHistory.Id), x => x.Action = "Tampered");
        await AssertHistoryModificationFailsAsync(c => c.WhatsAppConversationParticipantHistories.Where(x => x.Id == participantHistory.Id), x => x.Action = "Tampered");
        await AssertHistoryModificationFailsAsync(c => c.WhatsAppConversationEngagementScopeHistories.Where(x => x.Id == scopeHistory.Id), x => x.Action = "Tampered");

        await using (var deleteHistory = Db())
        {
            var row = await deleteHistory.WhatsAppConversationHistories.SingleAsync(x => x.Id == conversationHistory.Id);
            deleteHistory.Remove(row);
            await Assert.ThrowsAsync<InvalidOperationException>(() => deleteHistory.SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task ConversationRecordVersionProvidesOptimisticConcurrency()
    {
        await using var db = await Fresh();
        var conversation = await AddGroupConversationAsync(db, "concurrency-phase9a");
        await using var first = Db();
        await using var second = Db();
        var firstRow = await first.WhatsAppConversations.SingleAsync(x => x.Id == conversation.Id);
        var secondRow = await second.WhatsAppConversations.SingleAsync(x => x.Id == conversation.Id);
        Assert.Equal(firstRow.Version, secondRow.Version);

        firstRow.Status = WhatsAppConversationStatus.NeedsAuthorizationReview;
        await first.SaveChangesAsync();
        secondRow.AuthorizationVersion = 2;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task ConversationPersistenceLeavesFinancialAndWorkflowRecordsUnchangedAndDoesNotBackfill()
    {
        await using var db = await Fresh();
        var engagementId = await Engagement(db);
        var billing = new BillingService(db);
        var billingRecord = await billing.Generate(engagementId, new(2026, 1, 1), new(2026, 1, 31), BillingGenerationMode.Scheduled);
        var worker = new Worker { Name = "Phase 9A boundary worker", Type = WorkerType.Freelancer };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        await billing.Assign(billingRecord.WorkItem.Id, worker.Id, 50m);
        var assignment = await db.WorkerAssignments.SingleAsync(x => x.WorkItemId == billingRecord.WorkItem.Id);
        await billing.Pay(worker.Id, new(2026, 1, 31), "PHASE9A-PAYMENT", Guid.NewGuid(), new Dictionary<int, decimal> { [assignment.Id] = 100m });
        var invoices = new InvoiceService(db);
        var invoice = await invoices.CreateInvoice(
            InvoiceFlow.AccountingFirmToCustomer,
            "PHASE9A-INVOICE",
            new(2026, 1, 31),
            new Dictionary<int, decimal> { [billingRecord.Id] = billingRecord.Amount });
        var receipt = await invoices.CreateReceipt(
            new(2026, 2, 1),
            "PHASE9A-RECEIPT",
            Guid.NewGuid(),
            new Dictionary<int, decimal> { [invoice.Id] = 100m });
        db.ChangeTracker.Clear();

        var beforeBilling = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == billingRecord.Id);
        var beforeShares = await db.RevenueShareAllocations.AsNoTracking().Where(x => x.BillingRecordId == billingRecord.Id).OrderBy(x => x.Kind).ToListAsync();
        var beforeWorkItem = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == billingRecord.WorkItem.Id);
        var beforeAssignment = await db.WorkerAssignments.AsNoTracking().SingleAsync(x => x.Id == assignment.Id);
        var beforeInvoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        var beforeReceipt = await db.CustomerReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        var beforePayment = await db.WorkerPayments.AsNoTracking().SingleAsync(x => x.WorkerId == worker.Id);

        var contact = await AddContactAsync(db, "boundary-phase9a-contact");
        var address = await AddContactAddressAsync(db, contact.Id, "+60139000007");

        Assert.Equal(0, await db.WhatsAppConversations.CountAsync());
        var conversation = await AddDirectConversationAsync(db, "boundary-phase9a-conversation", address.Id);
        var scope = new WhatsAppConversationEngagementScope
        {
            WhatsAppConversationId = conversation.Id,
            EngagementId = engagementId,
            ApprovedAuthorizationVersion = 1,
            ApprovedAt = ConversationTestTime,
            ApprovedByActor = "manager"
        };
        db.WhatsAppConversationEngagementScopes.Add(scope);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.BillingRecords.CountAsync());
        Assert.Equal(billingRecord.WorkItem.Id, await db.WorkItems.Select(x => x.Id).SingleAsync());
        var afterBilling = await db.BillingRecords.AsNoTracking().SingleAsync(x => x.Id == billingRecord.Id);
        var afterShares = await db.RevenueShareAllocations.AsNoTracking().Where(x => x.BillingRecordId == billingRecord.Id).OrderBy(x => x.Kind).ToListAsync();
        var afterWorkItem = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == billingRecord.WorkItem.Id);
        var afterAssignment = await db.WorkerAssignments.AsNoTracking().SingleAsync(x => x.Id == assignment.Id);
        var afterInvoice = await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        var afterReceipt = await db.CustomerReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
        var afterPayment = await db.WorkerPayments.AsNoTracking().SingleAsync(x => x.Id == beforePayment.Id);
        Assert.Equal(beforeBilling.Amount, afterBilling.Amount);
        Assert.Equal(beforeBilling.Status, afterBilling.Status);
        Assert.Equal(beforeBilling.Version, afterBilling.Version);
        Assert.Equal(beforeShares.Select(x => (x.Kind, x.Amount, x.Percent)), afterShares.Select(x => (x.Kind, x.Amount, x.Percent)));
        Assert.Equal(beforeWorkItem.Status, afterWorkItem.Status);
        Assert.Equal(beforeWorkItem.Version, afterWorkItem.Version);
        Assert.Equal(beforeAssignment.Entitlement, afterAssignment.Entitlement);
        Assert.Equal(beforeAssignment.Version, afterAssignment.Version);
        Assert.Equal(beforeInvoice.Total, afterInvoice.Total);
        Assert.Equal(beforeInvoice.Status, afterInvoice.Status);
        Assert.Equal(beforeInvoice.Version, afterInvoice.Version);
        Assert.Equal(beforeReceipt.Amount, afterReceipt.Amount);
        Assert.Equal(beforeReceipt.IsCancelled, afterReceipt.IsCancelled);
        Assert.Equal(beforeReceipt.Version, afterReceipt.Version);
        Assert.Equal(beforePayment.Amount, afterPayment.Amount);
        Assert.Equal(beforePayment.IsCancelled, afterPayment.IsCancelled);
        Assert.Equal(beforePayment.Version, afterPayment.Version);
        Assert.Equal(1, await db.Engagements.CountAsync(x => x.Id == engagementId));
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(WorkItem))!.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(WhatsAppConversation));
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(BillingRecord))!.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(WhatsAppConversation));
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(WhatsAppConversationEngagementScope))!.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ContactCustomerLink));
    }
}
