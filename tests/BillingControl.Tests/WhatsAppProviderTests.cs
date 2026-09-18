using BillingControl.Services.WhatsApp;

namespace BillingControl.Tests;

public sealed class WhatsAppProviderTests
{
    private static readonly WhatsAppProviderAccountBinding Account =
        new("fake", "business-sender", "account-reference");

    [Fact]
    public async Task FakeAdvertisesDirectSupportAtRuntime()
    {
        var provider = new DeterministicFakeWhatsAppProvider();

        var capabilities = await provider.GetCapabilitiesAsync(Account);

        Assert.True(capabilities.SupportsDirectMessaging);
        Assert.True(capabilities.SupportsText);
        Assert.False(capabilities.SupportsGroupMessaging);
    }

    [Fact]
    public async Task FakeCanAdvertiseGroupUnsupported()
    {
        var provider = new DeterministicFakeWhatsAppProvider(
            new WhatsAppProviderCapabilities(
                supportsDirectMessaging: true,
                supportsGroupMessaging: false,
                supportsText: true));

        var capabilities = await provider.GetCapabilitiesAsync(Account);

        Assert.False(capabilities.SupportsGroupMessaging);
    }

    [Fact]
    public async Task GroupSendIsDefinitelyRejectedWhenCapabilityIsUnavailable()
    {
        var provider = new DeterministicFakeWhatsAppProvider
        {
            DefaultBehavior = FakeWhatsAppSendBehavior.Accepted()
        };
        var request = Request(
            "group-key",
            WhatsAppDestination.Group("opaque-group-thread"),
            new WhatsAppTextContent("Please send the documents."));

        var result = await provider.SendAsync(request);

        Assert.Equal(WhatsAppSendDisposition.DefinitelyRejected, result.Disposition);
        Assert.Equal("UnsupportedCapability", result.ErrorCategory);
        Assert.Equal("GroupMessagingNotSupported", result.ErrorCode);
        Assert.False(result.IsAccepted);
        Assert.Single(provider.CapturedRequests);
    }

    [Fact]
    public async Task AcceptedSendReturnsDeterministicProviderMessageId()
    {
        var provider = new DeterministicFakeWhatsAppProvider();

        var result = await provider.SendAsync(Request(
            "logical-accepted",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("Hello.")));

        Assert.Equal(WhatsAppSendDisposition.Accepted, result.Disposition);
        Assert.True(result.IsAccepted);
        Assert.Equal("fake-provider-message-0001", result.ProviderMessageId);
    }

    [Fact]
    public async Task DefinitelyRejectedAndAmbiguousAreDistinctOutcomes()
    {
        var provider = new DeterministicFakeWhatsAppProvider();
        provider.EnqueueBehavior(FakeWhatsAppSendBehavior.DefinitelyRejected(
            "RateLimited", "rate-limit", TimeSpan.FromSeconds(30)));
        provider.EnqueueBehavior(FakeWhatsAppSendBehavior.Ambiguous(
            "Network", "timeout"));

        var rejected = await provider.SendAsync(Request(
            "logical-rejected",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("First.")));
        var ambiguous = await provider.SendAsync(Request(
            "logical-ambiguous",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("Second.")));

        Assert.Equal(WhatsAppSendDisposition.DefinitelyRejected, rejected.Disposition);
        Assert.True(rejected.IsDefinitelyRejected);
        Assert.Equal(WhatsAppSendDisposition.Ambiguous, ambiguous.Disposition);
        Assert.True(ambiguous.IsAmbiguous);
        Assert.False(ambiguous.IsAccepted);
    }

    [Fact]
    public async Task AmbiguousResultMakesNoFalseAcceptanceClaim()
    {
        var provider = new DeterministicFakeWhatsAppProvider
        {
            DefaultBehavior = FakeWhatsAppSendBehavior.Ambiguous("Network", "timeout")
        };

        var result = await provider.SendAsync(Request(
            "logical-ambiguous",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("May or may not have arrived.")));

        Assert.True(result.IsAmbiguous);
        Assert.False(result.IsAccepted);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task RetryAfterHintIsPreserved()
    {
        var retryAfter = TimeSpan.FromMinutes(2);
        var provider = new DeterministicFakeWhatsAppProvider
        {
            DefaultBehavior = FakeWhatsAppSendBehavior.DefinitelyRejected(
                "RateLimited", "too-many-requests", retryAfter)
        };

        var result = await provider.SendAsync(Request(
            "logical-rate-limited",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("Try later.")));

        Assert.Equal(WhatsAppSendDisposition.DefinitelyRejected, result.Disposition);
        Assert.Equal(retryAfter, result.RetryAfter);
        Assert.Equal("RateLimited", result.ErrorCategory);
        Assert.Equal("too-many-requests", result.ErrorCode);
    }

    [Fact]
    public async Task CapturedRequestPreservesLogicalMessageKeyAndCorrelationId()
    {
        var provider = new DeterministicFakeWhatsAppProvider();
        var request = Request(
            "logical-immutable-key",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("Exact text."),
            "correlation-123");

        await provider.SendAsync(request);

        var captured = Assert.Single(provider.CapturedRequests);
        Assert.Same(request, captured);
        Assert.Equal("logical-immutable-key", captured.LogicalMessageKey);
        Assert.Equal("correlation-123", captured.CorrelationId);
        Assert.Equal("business-sender", captured.BusinessEndpointKey);
    }

    [Fact]
    public async Task TextContentIsPreservedExactly()
    {
        var provider = new DeterministicFakeWhatsAppProvider();
        const string text = "  Exact text with punctuation: #8A.  ";

        await provider.SendAsync(Request(
            "logical-text",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent(text)));

        var captured = Assert.Single(provider.CapturedRequests);
        var content = Assert.IsType<WhatsAppTextContent>(captured.ContentSnapshot);
        Assert.Equal(text, content.Text);
        Assert.Equal(text, content.Body);
    }

    [Fact]
    public async Task TemplateContentLanguageAndParametersAreSnapshotted()
    {
        var parameters = new List<string> { "ABC", "2026-09" };
        var content = new WhatsAppTemplateContent("document-request", "en-MY", parameters);
        var provider = new DeterministicFakeWhatsAppProvider(
            new WhatsAppProviderCapabilities(supportsTemplateMessaging: true));

        parameters[0] = "changed-after-request-construction";
        await provider.SendAsync(Request("logical-template", WhatsAppDestination.Direct("opaque-direct-recipient"), content));

        var captured = Assert.Single(provider.CapturedRequests);
        var template = Assert.IsType<WhatsAppTemplateContent>(captured.Content);
        Assert.Equal("document-request", template.TemplateName);
        Assert.Equal("document-request", template.TemplateKey);
        Assert.Equal("en-MY", template.Language);
        Assert.Equal("en-MY", template.LanguageCode);
        Assert.Equal(["ABC", "2026-09"], template.Parameters);
    }

    [Fact]
    public async Task RepeatedLogicalMessageKeyCallsAreCapturedSeparately()
    {
        var provider = new DeterministicFakeWhatsAppProvider();
        var request = Request(
            "same-logical-key",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("No fake deduplication."));

        var first = await provider.SendAsync(request);
        var second = await provider.SendAsync(request);

        Assert.Equal("fake-provider-message-0001", first.ProviderMessageId);
        Assert.Equal("fake-provider-message-0002", second.ProviderMessageId);
        Assert.Equal(2, provider.CapturedRequests.Count(x => x.LogicalMessageKey == "same-logical-key"));
    }

    [Fact]
    public async Task FakeSendDoesNotRequireARealNetwork()
    {
        var provider = new FakeWhatsAppProvider();

        var result = await provider.SendAsync(Request(
            "logical-no-network",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTextContent("CI only.")));

        Assert.Equal(WhatsAppSendDisposition.Accepted, result.Disposition);
    }

    [Fact]
    public void AccountBindingRequiresOnlyNonSecretIdentityReferences()
    {
        var binding = new WhatsAppProviderAccountBinding("fake", "business-sender", "account-reference");
        var names = binding.GetType().GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal("fake", binding.ProviderName);
        Assert.Equal("business-sender", binding.BusinessEndpointKey);
        Assert.Equal("account-reference", binding.ProviderAccountReference);
        Assert.DoesNotContain(names, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Certificate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DestinationIdentifiersRemainOpaqueAndProviderNeutral()
    {
        var direct = WhatsAppDestination.Direct(
            "opaque-direct-key",
            "+60123456789",
            "opaque-provider-recipient-key");
        var group = WhatsAppDestination.Group("opaque-conversation-thread-key");

        Assert.Equal(WhatsAppDestinationKind.Direct, direct.Kind);
        Assert.Equal("opaque-direct-key", direct.ProviderDestinationKey);
        Assert.Equal("+60123456789", direct.NormalizedE164);
        Assert.Equal("opaque-provider-recipient-key", direct.ProviderRecipientKey);
        Assert.Equal(WhatsAppDestinationKind.Group, group.Kind);
        Assert.Equal("opaque-conversation-thread-key", group.ProviderConversationKey);
        Assert.DoesNotContain("+60123456789", direct.ToString());
    }

    [Fact]
    public void SensitiveContentIsNotPrintedByContractDtos()
    {
        const string body = "Sensitive client message body";
        var content = new WhatsAppTextContent(body);
        var request = Request(
            "logical-redaction",
            WhatsAppDestination.Direct("+60123456789"),
            content);

        Assert.DoesNotContain(body, content.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(body, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("+60123456789", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("logical-redaction", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("correlation-default", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedTemplateCapabilityIsDefinitelyRejected()
    {
        var provider = new DeterministicFakeWhatsAppProvider();

        var result = await provider.SendAsync(Request(
            "logical-template-unsupported",
            WhatsAppDestination.Direct("opaque-direct-recipient"),
            new WhatsAppTemplateContent("document-request", "en", ["one"])));

        Assert.Equal(WhatsAppSendDisposition.DefinitelyRejected, result.Disposition);
        Assert.Equal("TemplateMessagingNotSupported", result.ErrorCode);
        var capturedTemplate = Assert.IsType<WhatsAppTemplateContent>(
            Assert.Single(provider.CapturedRequests).ContentSnapshot);
        Assert.Equal(["one"], capturedTemplate.Parameters);
    }

    private static WhatsAppOutboundRequest Request(
        string logicalMessageKey,
        WhatsAppDestination destination,
        WhatsAppOutboundContent content,
        string correlationId = "correlation-default") =>
        new(logicalMessageKey, Account, destination, content, correlationId);
}
