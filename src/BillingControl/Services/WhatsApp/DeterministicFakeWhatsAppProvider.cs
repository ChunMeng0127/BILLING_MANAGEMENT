using System.Collections.ObjectModel;

namespace BillingControl.Services.WhatsApp;

/// <summary>
/// A safe, deterministic outcome configuration for the CI fake. It has no raw
/// provider payload or retry loop.
/// </summary>
public sealed record FakeWhatsAppSendBehavior
{
    public WhatsAppSendDisposition Disposition { get; }
    public string? ErrorCategory { get; }
    public string? ErrorCode { get; }
    public TimeSpan? RetryAfter { get; }
    public DateTimeOffset? ProviderTimestamp { get; }

    private FakeWhatsAppSendBehavior(
        WhatsAppSendDisposition disposition,
        string? errorCategory,
        string? errorCode,
        TimeSpan? retryAfter,
        DateTimeOffset? providerTimestamp)
    {
        if (disposition == WhatsAppSendDisposition.Accepted && (errorCategory is not null || errorCode is not null))
            throw new ArgumentException("An accepted fake outcome cannot contain a provider error.");

        WhatsAppContractValidation.OptionalReference(errorCategory, nameof(errorCategory));
        WhatsAppContractValidation.OptionalReference(errorCode, nameof(errorCode));
        if (retryAfter.HasValue && retryAfter.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryAfter), "RetryAfter cannot be negative.");

        Disposition = disposition;
        ErrorCategory = errorCategory;
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
        ProviderTimestamp = providerTimestamp;
    }

    public static FakeWhatsAppSendBehavior Accepted(DateTimeOffset? providerTimestamp = null) =>
        new(WhatsAppSendDisposition.Accepted, null, null, null, providerTimestamp);

    public static FakeWhatsAppSendBehavior DefinitelyRejected(
        string errorCategory = "ProviderRejected",
        string? errorCode = null,
        TimeSpan? retryAfter = null,
        DateTimeOffset? providerTimestamp = null) =>
        new(WhatsAppSendDisposition.DefinitelyRejected, errorCategory, errorCode, retryAfter, providerTimestamp);

    public static FakeWhatsAppSendBehavior Ambiguous(
        string errorCategory = "UnknownOutcome",
        string? errorCode = null,
        TimeSpan? retryAfter = null,
        DateTimeOffset? providerTimestamp = null) =>
        new(WhatsAppSendDisposition.Ambiguous, errorCategory, errorCode, retryAfter, providerTimestamp);
}

/// <summary>
/// Deterministic provider fake for unit/CI tests. It has no network dependency,
/// records every call, and deliberately does not deduplicate logical message keys.
/// </summary>
public class DeterministicFakeWhatsAppProvider : IWhatsAppProvider
{
    private readonly object _gate = new();
    private readonly List<WhatsAppOutboundRequest> _capturedRequests = [];
    private readonly Queue<FakeWhatsAppSendBehavior> _queuedBehaviors = [];
    private WhatsAppProviderCapabilities _capabilities;
    private FakeWhatsAppSendBehavior _defaultBehavior = FakeWhatsAppSendBehavior.Accepted();
    private int _acceptedMessageSequence;

    public DeterministicFakeWhatsAppProvider(WhatsAppProviderCapabilities? capabilities = null)
    {
        _capabilities = capabilities ?? new WhatsAppProviderCapabilities();
    }

    public WhatsAppProviderCapabilities Capabilities
    {
        get { lock (_gate) return _capabilities; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate) _capabilities = value;
        }
    }

    public FakeWhatsAppSendBehavior DefaultBehavior
    {
        get { lock (_gate) return _defaultBehavior; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_gate) _defaultBehavior = value;
        }
    }

    public IReadOnlyList<WhatsAppOutboundRequest> CapturedRequests
    {
        get
        {
            lock (_gate)
                return new ReadOnlyCollection<WhatsAppOutboundRequest>(_capturedRequests.ToArray());
        }
    }

    public IReadOnlyList<WhatsAppOutboundRequest> Requests => CapturedRequests;

    public Task<WhatsAppProviderCapabilities> GetCapabilitiesAsync(
        WhatsAppProviderAccountBinding accountBinding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountBinding);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Capabilities);
    }

    public Task<WhatsAppSendResult> SendAsync(
        WhatsAppOutboundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // Capture before capability validation so tests can see every attempted
            // outbound request, including a deterministic unsupported-capability failure.
            _capturedRequests.Add(request);

            var capabilityFailure = ValidateCapabilities(request, _capabilities);
            if (capabilityFailure is not null)
                return Task.FromResult(capabilityFailure);

            var behavior = _queuedBehaviors.Count > 0
                ? _queuedBehaviors.Dequeue()
                : _defaultBehavior;

            return Task.FromResult(CreateResult(behavior));
        }
    }

    public void EnqueueBehavior(FakeWhatsAppSendBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        lock (_gate) _queuedBehaviors.Enqueue(behavior);
    }

    public void ClearCapturedRequests()
    {
        lock (_gate) _capturedRequests.Clear();
    }

    private WhatsAppSendResult CreateResult(FakeWhatsAppSendBehavior behavior)
    {
        return behavior.Disposition switch
        {
            WhatsAppSendDisposition.Accepted =>
                WhatsAppSendResult.Accepted(
                    $"fake-provider-message-{++_acceptedMessageSequence:0000}",
                    behavior.ProviderTimestamp),
            WhatsAppSendDisposition.DefinitelyRejected =>
                WhatsAppSendResult.DefinitelyRejected(
                    behavior.ErrorCategory ?? "ProviderRejected",
                    behavior.ErrorCode,
                    behavior.RetryAfter,
                    behavior.ProviderTimestamp),
            WhatsAppSendDisposition.Ambiguous =>
                WhatsAppSendResult.Ambiguous(
                    behavior.ErrorCategory ?? "UnknownOutcome",
                    behavior.ErrorCode,
                    behavior.RetryAfter,
                    providerTimestamp: behavior.ProviderTimestamp),
            _ => throw new ArgumentOutOfRangeException(nameof(behavior.Disposition))
        };
    }

    private static WhatsAppSendResult? ValidateCapabilities(
        WhatsAppOutboundRequest request,
        WhatsAppProviderCapabilities capabilities)
    {
        if (request.Destination.Kind == WhatsAppDestinationKind.Direct && !capabilities.SupportsDirectMessaging)
            return WhatsAppSendResult.DefinitelyRejected("UnsupportedCapability", "DirectMessagingNotSupported");

        if (request.Destination.Kind == WhatsAppDestinationKind.Group && !capabilities.SupportsGroupMessaging)
            return WhatsAppSendResult.DefinitelyRejected("UnsupportedCapability", "GroupMessagingNotSupported");

        return request.Content.Kind switch
        {
            WhatsAppOutboundContentKind.Text when !capabilities.SupportsText =>
                WhatsAppSendResult.DefinitelyRejected("UnsupportedCapability", "TextContentNotSupported"),
            WhatsAppOutboundContentKind.Template when !capabilities.SupportsTemplateMessaging =>
                WhatsAppSendResult.DefinitelyRejected("UnsupportedCapability", "TemplateMessagingNotSupported"),
            _ => null
        };
    }
}

/// <summary>
/// Short discoverable alias for the deterministic CI fake.
/// </summary>
public sealed class FakeWhatsAppProvider : DeterministicFakeWhatsAppProvider
{
    public FakeWhatsAppProvider(WhatsAppProviderCapabilities? capabilities = null)
        : base(capabilities)
    {
    }
}
