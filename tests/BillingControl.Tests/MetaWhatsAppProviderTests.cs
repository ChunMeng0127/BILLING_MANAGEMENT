using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BillingControl.Services.WhatsApp;
using Microsoft.Extensions.Options;

namespace BillingControl.Tests;

public sealed class MetaWhatsAppProviderTests
{
    private const string TestAccessToken = "test-access-token-placeholder";

    private static readonly WhatsAppProviderAccountBinding Account =
        new("meta-test", "meta-test-sender", "business-account-test");

    [Fact]
    public async Task AcceptedTextMapsMetaMessageIdAndUsesConfiguredEndpoint()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            "{\"messaging_product\":\"whatsapp\",\"messages\":[{\"id\":\"wamid.test-text-001\"}]}")));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-text",
            WhatsAppDestination.Direct(
                "opaque-direct-key",
                normalizedE164: "+60123456789",
                providerRecipientKey: "meta-recipient-key"),
            new WhatsAppTextContent("Hello from the provider adapter.")));

        Assert.Equal(WhatsAppSendDisposition.Accepted, result.Disposition);
        Assert.Equal("wamid.test-text-001", result.ProviderMessageId);

        var captured = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal("https://graph.test/v23.0/phone-number-test/messages", captured.RequestUri.ToString());
        Assert.Equal(TestAccessToken, captured.BearerToken);

        using var body = JsonDocument.Parse(captured.Body);
        var root = body.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", root.GetProperty("recipient_type").GetString());
        Assert.Equal("meta-recipient-key", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Hello from the provider adapter.", root.GetProperty("text").GetProperty("body").GetString());
        Assert.False(root.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public async Task AcceptedTemplateMapsIdAndSendsApprovedBodyTextParameters()
    {
        var options = CreateOptions();
        options.SupportsTemplateMessaging = true;
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.test-template-001\"}]}")));
        using var fixture = CreateFixture(handler, options);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-template",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTemplateContent("document-request", "en_US", ["ABC-123", "2026-09"])));

        Assert.True(result.IsAccepted);
        Assert.Equal("wamid.test-template-001", result.ProviderMessageId);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("individual", body.RootElement.GetProperty("recipient_type").GetString());
        var template = body.RootElement.GetProperty("template");
        Assert.Equal("document-request", template.GetProperty("name").GetString());
        Assert.Equal("en_US", template.GetProperty("language").GetProperty("code").GetString());
        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(2, parameters.GetArrayLength());
        Assert.Equal("text", parameters[0].GetProperty("type").GetString());
        Assert.Equal("ABC-123", parameters[0].GetProperty("text").GetString());
        Assert.Equal("2026-09", parameters[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ProviderCapabilitiesComeFromRuntimeOptionsAndGroupDefaultsClosed()
    {
        var options = CreateOptions();
        options.SupportsTemplateMessaging = true;
        options.SupportsMedia = true;
        using var fixture = CreateFixture(new RecordingHandler(), options);

        var capabilities = await fixture.Provider.GetCapabilitiesAsync(Account);

        Assert.True(capabilities.SupportsDirectMessaging);
        Assert.True(capabilities.SupportsText);
        Assert.True(capabilities.SupportsTemplateMessaging);
        Assert.True(capabilities.SupportsMedia);
        Assert.False(capabilities.SupportsGroupMessaging);
        Assert.False(capabilities.SupportsDocuments);
    }

    [Fact]
    public async Task GroupSendIsDefinitelyRejectedWithoutConfiguredGroupCapability()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-group",
            WhatsAppDestination.Group("opaque-group-thread"),
            new WhatsAppTextContent("Group content.")));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("UnsupportedCapability", result.ErrorCategory);
        Assert.Equal("GroupMessagingNotSupported", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExplicitlyConfiguredGroupCapabilityControlsTheTransportAttempt()
    {
        var options = CreateOptions();
        options.SupportsGroupMessaging = true;
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.test-group-001\"}]}")));
        using var fixture = CreateFixture(handler, options);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-group-enabled",
            WhatsAppDestination.Group("configured-group-key"),
            new WhatsAppTextContent("Configured group content.")));

        Assert.True(result.IsAccepted);
        Assert.Equal("wamid.test-group-001", result.ProviderMessageId);
        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("group", body.RootElement.GetProperty("recipient_type").GetString());
        Assert.Equal("configured-group-key", body.RootElement.GetProperty("to").GetString());
    }

    [Fact]
    public async Task BadRequestMapsToDefinitelyRejectedProviderValidation()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"invalid parameter\",\"code\":100}}")));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-400",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Invalid request test.")));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("ProviderValidation", result.ErrorCategory);
        Assert.Equal("100", result.ErrorCode);
        Assert.Null(result.ProviderMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Authentication")]
    [InlineData(HttpStatusCode.Forbidden, "Authorization")]
    public async Task AuthenticationAndAuthorizationFailuresAreDefinitelyRejected(
        HttpStatusCode statusCode,
        string expectedCategory)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            statusCode,
            "{\"error\":{\"code\":190}}")));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-auth",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Auth test.")));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal(expectedCategory, result.ErrorCategory);
        Assert.Equal("190", result.ErrorCode);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task ThrottlingMapsToDefinitelyRejectedAndPreservesRetryAfter()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":130429}}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return Task.FromResult(response);
        });
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-429",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Rate limit test.")));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("RateLimited", result.ErrorCategory);
        Assert.Equal("130429", result.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(17), result.RetryAfter);
    }

    [Fact]
    public async Task ServerFailureIsAmbiguousAndPreservesRetryAfter()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = JsonResponse(HttpStatusCode.BadGateway, "{\"error\":{\"code\":1}}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(9));
            return Task.FromResult(response);
        });
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-5xx",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Server failure test.")));

        Assert.True(result.IsAmbiguous);
        Assert.Equal("ProviderUnavailable", result.ErrorCategory);
        Assert.Equal("1", result.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(9), result.RetryAfter);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task HttpRequestTimeoutIsAmbiguous()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("simulated transport timeout")));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-timeout",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Timeout test.")));

        Assert.True(result.IsAmbiguous);
        Assert.Equal("Timeout", result.ErrorCategory);
        Assert.Equal("ClientTimeout", result.ErrorCode);
    }

    [Fact]
    public async Task NetworkFailureIsAmbiguous()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated network failure")));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-network",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Network test.")));

        Assert.True(result.IsAmbiguous);
        Assert.Equal("Network", result.ErrorCategory);
        Assert.Equal("HttpRequestException", result.ErrorCode);
    }

    [Theory]
    [InlineData("not-json", "InvalidJson")]
    [InlineData("{\"messages\":[]}", "MissingMessageId")]
    [InlineData("{\"messages\":[{\"id\":\"\"}]}", "MissingMessageId")]
    public async Task MalformedOrIncompleteSuccessfulResponseDoesNotClaimAcceptance(
        string body,
        string expectedErrorCode)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-malformed",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Malformed response test.")));

        Assert.True(result.IsAmbiguous);
        Assert.Equal("MalformedProviderResponse", result.ErrorCategory);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Null(result.ProviderMessageId);
    }

    [Fact]
    public async Task RequestTimeoutStatusIsAmbiguous()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.RequestTimeout,
            "{\"error\":{\"code\":408}}")));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-http-408",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("HTTP timeout test.")));

        Assert.True(result.IsAmbiguous);
        Assert.Equal("Timeout", result.ErrorCategory);
        Assert.Equal("408", result.ErrorCode);
    }

    [Fact]
    public async Task UnsupportedDirectAndTextCapabilitiesDoNotCallHttp()
    {
        var options = CreateOptions();
        options.SupportsDirectMessaging = false;
        options.SupportsText = false;
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        using var fixture = CreateFixture(handler, options);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-direct-unsupported",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Unsupported test.")));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("UnsupportedCapability", result.ErrorCategory);
        Assert.Equal("DirectMessagingNotSupported", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnsupportedTemplateCapabilityDoesNotCallHttp()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        using var fixture = CreateFixture(handler);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-template-unsupported",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTemplateContent("document-request", "en_US", ["one"])));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("UnsupportedCapability", result.ErrorCategory);
        Assert.Equal("TemplateMessagingNotSupported", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedAndNotMisclassified()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation delay unexpectedly completed.");
        });
        handler.Started = started;
        using var fixture = CreateFixture(handler);
        using var cancellation = new CancellationTokenSource();

        var send = fixture.Provider.SendAsync(Request(
            "logical-cancelled",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Cancellation test.")), cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RepeatedLogicalMessageKeyCallsBothReachMetaTransport()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.test-duplicate-001\"}]}")));
        using var fixture = CreateFixture(handler);
        var request = Request(
            "same-logical-key",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("No provider-side deduplication."));

        await fixture.Provider.SendAsync(request);
        await fixture.Provider.SendAsync(request);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task BindingMismatchIsConfigurationRejectionWithoutHttpCall()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        using var fixture = CreateFixture(handler);
        var wrongAccount = new WhatsAppProviderAccountBinding(
            "meta-test",
            "different-sender",
            "business-account-test");

        var result = await fixture.Provider.SendAsync(new WhatsAppOutboundRequest(
            "logical-binding-mismatch",
            wrongAccount,
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Configuration test."),
            "correlation-binding-mismatch"));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("Configuration", result.ErrorCategory);
        Assert.Equal("AccountBindingMismatch", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MissingConfigurationIsSafeRejectionWithoutHttpCall()
    {
        var options = CreateOptions();
        options.ApiVersion = string.Empty;
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP must not be called"));
        using var fixture = CreateFixture(handler, options);

        var result = await fixture.Provider.SendAsync(Request(
            "logical-missing-config",
            WhatsAppDestination.Direct("meta-recipient-key"),
            new WhatsAppTextContent("Configuration test.")));

        Assert.True(result.IsDefinitelyRejected);
        Assert.Equal("Configuration", result.ErrorCategory);
        Assert.Equal("MissingOrInvalidApiVersion", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void OptionsRedactionDoesNotExposeTokenOrIdentifiers()
    {
        var options = CreateOptions();

        var text = options.ToString();

        Assert.DoesNotContain(TestAccessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(options.BusinessAccountId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(options.PhoneNumberId, text, StringComparison.Ordinal);
        Assert.DoesNotContain(options.BusinessEndpointKey, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderConstructorContainsOnlyTransportConfigurationDependencies()
    {
        var constructor = Assert.Single(typeof(MetaWhatsAppProvider).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Equal(
            [typeof(HttpClient), typeof(IOptions<MetaWhatsAppOptions>), typeof(TimeProvider)],
            parameterTypes);
    }

    private static ProviderFixture CreateFixture(
        RecordingHandler handler,
        MetaWhatsAppOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var provider = new MetaWhatsAppProvider(client, Options.Create(options ?? CreateOptions()), timeProvider);
        return new ProviderFixture(provider, client);
    }

    private static MetaWhatsAppOptions CreateOptions() => new()
    {
        GraphBaseUrl = "https://graph.test",
        ApiVersion = "v23.0",
        AccessToken = TestAccessToken,
        BusinessAccountId = "business-account-test",
        PhoneNumberId = "phone-number-test",
        BusinessEndpointKey = "meta-test-sender"
    };

    private static WhatsAppOutboundRequest Request(
        string logicalMessageKey,
        WhatsAppDestination destination,
        WhatsAppOutboundContent content) =>
        new(logicalMessageKey, Account, destination, content, $"correlation-{logicalMessageKey}");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class ProviderFixture : IDisposable
    {
        public ProviderFixture(MetaWhatsAppProvider provider, HttpClient client)
        {
            Provider = provider;
            Client = client;
        }

        public MetaWhatsAppProvider Provider { get; }
        private HttpClient Client { get; }

        public void Dispose() => Client.Dispose();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        private readonly List<CapturedRequest> _requests = [];

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
        {
            _responder = responder ?? ((_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                "{\"messages\":[{\"id\":\"wamid.default\"}]}")));
        }

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        public TaskCompletionSource<bool>? Started { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri ?? throw new InvalidOperationException("A request URI is required."),
                request.Headers.Authorization?.Parameter,
                body));
            Started?.TrySetResult(true);
            return await _responder(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? BearerToken,
        string Body);
}
