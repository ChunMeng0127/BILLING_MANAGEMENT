using System.Collections.ObjectModel;

namespace BillingControl.Services.WhatsApp;

/// <summary>
/// The kind of provider-neutral WhatsApp destination being addressed.
/// </summary>
public enum WhatsAppDestinationKind
{
    Direct,
    Group
}

/// <summary>
/// The provider-neutral account and sender binding used by an application operation.
/// This DTO intentionally contains no credentials or credential material.
/// </summary>
public sealed record WhatsAppProviderAccountBinding
{
    public string ProviderName { get; }
    public string BusinessEndpointKey { get; }
    public string? ProviderAccountReference { get; }

    public WhatsAppProviderAccountBinding(
        string providerName,
        string businessEndpointKey,
        string? providerAccountReference = null)
    {
        WhatsAppContractValidation.RequiredReference(providerName, nameof(providerName));
        WhatsAppContractValidation.RequiredReference(businessEndpointKey, nameof(businessEndpointKey));
        WhatsAppContractValidation.OptionalReference(providerAccountReference, nameof(providerAccountReference));

        ProviderName = providerName;
        BusinessEndpointKey = businessEndpointKey;
        ProviderAccountReference = providerAccountReference;
    }

    public override string ToString() =>
        $"WhatsAppProviderAccountBinding(ProviderName={ProviderName}, BusinessEndpointKey=[redacted], ProviderAccountReference={(!string.IsNullOrWhiteSpace(ProviderAccountReference) ? "present" : "none")})";
}

/// <summary>
/// An opaque provider-neutral destination. Direct destinations may retain the
/// application's normalized number and an opaque provider recipient reference as
/// mapping evidence; application code does not parse the provider reference.
/// Group destinations use an opaque provider conversation/thread key.
/// </summary>
public sealed record WhatsAppDestination
{
    public WhatsAppDestinationKind Kind { get; }
    public string ProviderDestinationKey { get; }
    public string? NormalizedE164 { get; }
    public string? ProviderRecipientKey { get; }

    public string OpaqueDestinationKey => ProviderDestinationKey;
    public string? ProviderConversationKey => Kind == WhatsAppDestinationKind.Group ? ProviderDestinationKey : null;

    private WhatsAppDestination(
        WhatsAppDestinationKind kind,
        string providerDestinationKey,
        string? normalizedE164,
        string? providerRecipientKey)
    {
        WhatsAppContractValidation.RequiredReference(providerDestinationKey, nameof(providerDestinationKey));
        WhatsAppContractValidation.OptionalReference(normalizedE164, nameof(normalizedE164));
        WhatsAppContractValidation.OptionalReference(providerRecipientKey, nameof(providerRecipientKey));

        if (kind == WhatsAppDestinationKind.Group && (normalizedE164 is not null || providerRecipientKey is not null))
            throw new ArgumentException("Group destinations cannot carry direct-recipient mapping values.");

        Kind = kind;
        ProviderDestinationKey = providerDestinationKey;
        NormalizedE164 = normalizedE164;
        ProviderRecipientKey = providerRecipientKey;
    }

    public static WhatsAppDestination Direct(
        string providerDestinationKey,
        string? normalizedE164 = null,
        string? providerRecipientKey = null) =>
        new(WhatsAppDestinationKind.Direct, providerDestinationKey, normalizedE164, providerRecipientKey);

    public static WhatsAppDestination Group(string providerConversationKey) =>
        new(WhatsAppDestinationKind.Group, providerConversationKey, null, null);

    public override string ToString() =>
        $"WhatsAppDestination(Kind={Kind}, ProviderDestinationKey=[redacted], HasNormalizedE164={!string.IsNullOrWhiteSpace(NormalizedE164)}, HasProviderRecipientKey={!string.IsNullOrWhiteSpace(ProviderRecipientKey)})";
}

/// <summary>
/// Runtime capabilities reported by a provider/account binding. Group support is
/// deliberately false by default and must be advertised by the implementation.
/// </summary>
public sealed record WhatsAppProviderCapabilities
{
    public bool SupportsDirectMessaging { get; }
    public bool SupportsGroupMessaging { get; }
    public bool SupportsText { get; }
    public bool SupportsTemplateMessaging { get; }
    public bool SupportsMedia { get; }
    public bool SupportsDocuments { get; }
    public bool SupportsDocument => SupportsDocuments;

    public WhatsAppProviderCapabilities(
        bool supportsDirectMessaging = true,
        bool supportsGroupMessaging = false,
        bool supportsText = true,
        bool supportsTemplateMessaging = false,
        bool supportsMedia = false,
        bool supportsDocuments = false)
    {
        SupportsDirectMessaging = supportsDirectMessaging;
        SupportsGroupMessaging = supportsGroupMessaging;
        SupportsText = supportsText;
        SupportsTemplateMessaging = supportsTemplateMessaging;
        SupportsMedia = supportsMedia;
        SupportsDocuments = supportsDocuments;
    }

    public override string ToString() =>
        $"WhatsAppProviderCapabilities(Direct={SupportsDirectMessaging}, Group={SupportsGroupMessaging}, Text={SupportsText}, Template={SupportsTemplateMessaging}, Media={SupportsMedia}, Documents={SupportsDocuments})";
}

public enum WhatsAppOutboundContentKind
{
    Text,
    Template
}

/// <summary>
/// Immutable provider-neutral outbound content. Only the content concepts needed
/// by the manual/test sending phase are represented here.
/// </summary>
public abstract record WhatsAppOutboundContent
{
    internal WhatsAppOutboundContent() { }

    public abstract WhatsAppOutboundContentKind Kind { get; }
}

public sealed record WhatsAppTextContent : WhatsAppOutboundContent
{
    public string Text { get; }
    public string Body => Text;
    public override WhatsAppOutboundContentKind Kind => WhatsAppOutboundContentKind.Text;

    public WhatsAppTextContent(string text)
    {
        WhatsAppContractValidation.RequiredReference(text, nameof(text));
        Text = text;
    }

    public override string ToString() => "WhatsAppTextContent(Text=[redacted])";
}

public sealed record WhatsAppTemplateContent : WhatsAppOutboundContent
{
    public string TemplateName { get; }
    public string TemplateKey => TemplateName;
    public string Language { get; }
    public string LanguageCode => Language;
    public IReadOnlyList<string> Parameters { get; }
    public override WhatsAppOutboundContentKind Kind => WhatsAppOutboundContentKind.Template;

    public WhatsAppTemplateContent(
        string templateName,
        string language,
        IEnumerable<string> parameters)
    {
        WhatsAppContractValidation.RequiredReference(templateName, nameof(templateName));
        WhatsAppContractValidation.RequiredReference(language, nameof(language));
        ArgumentNullException.ThrowIfNull(parameters);

        var snapshot = parameters.Select((parameter, index) =>
        {
            WhatsAppContractValidation.RequiredReference(parameter, $"{nameof(parameters)}[{index}]");
            return parameter;
        }).ToArray();

        TemplateName = templateName;
        Language = language;
        Parameters = new ReadOnlyCollection<string>(snapshot);
    }

    public override string ToString() =>
        $"WhatsAppTemplateContent(TemplateName=[redacted], Language={Language}, ParameterCount={Parameters.Count})";
}

public enum WhatsAppSendDisposition
{
    Accepted,
    DefinitelyRejected,
    Ambiguous
}

/// <summary>
/// The provider outcome of one send attempt. Ambiguous is intentionally distinct
/// from rejection: it means the provider/network outcome does not prove whether
/// the provider accepted the message, so this abstraction makes no acceptance
/// claim and does not prescribe a retry.
/// </summary>
public sealed record WhatsAppSendResult
{
    public WhatsAppSendDisposition Disposition { get; }
    public string? ProviderMessageId { get; }
    public string? ProviderMessageReference => ProviderMessageId;
    public string? ErrorCategory { get; }
    public string? ErrorCode { get; }
    public TimeSpan? RetryAfter { get; }
    public DateTimeOffset? ProviderTimestamp { get; }

    public bool IsAccepted => Disposition == WhatsAppSendDisposition.Accepted;
    public bool IsDefinitelyRejected => Disposition == WhatsAppSendDisposition.DefinitelyRejected;
    public bool IsAmbiguous => Disposition == WhatsAppSendDisposition.Ambiguous;

    private WhatsAppSendResult(
        WhatsAppSendDisposition disposition,
        string? providerMessageId,
        string? errorCategory,
        string? errorCode,
        TimeSpan? retryAfter,
        DateTimeOffset? providerTimestamp)
    {
        WhatsAppContractValidation.OptionalReference(providerMessageId, nameof(providerMessageId));
        WhatsAppContractValidation.OptionalReference(errorCategory, nameof(errorCategory));
        WhatsAppContractValidation.OptionalReference(errorCode, nameof(errorCode));
        ValidateRetryAfter(retryAfter);

        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition), "Unknown WhatsApp send disposition.");

        if (disposition == WhatsAppSendDisposition.Accepted && (errorCategory is not null || errorCode is not null))
            throw new ArgumentException("An accepted send result cannot contain a provider error.");

        if (disposition == WhatsAppSendDisposition.DefinitelyRejected && providerMessageId is not null)
            throw new ArgumentException("A definitely rejected send result cannot claim a provider message id.");

        Disposition = disposition;
        ProviderMessageId = providerMessageId;
        ErrorCategory = errorCategory;
        ErrorCode = errorCode;
        RetryAfter = retryAfter;
        ProviderTimestamp = providerTimestamp;
    }

    public static WhatsAppSendResult Accepted(
        string? providerMessageId = null,
        DateTimeOffset? providerTimestamp = null) =>
        new(WhatsAppSendDisposition.Accepted, providerMessageId, null, null, null, providerTimestamp);

    public static WhatsAppSendResult DefinitelyRejected(
        string errorCategory = "ProviderRejected",
        string? errorCode = null,
        TimeSpan? retryAfter = null,
        DateTimeOffset? providerTimestamp = null) =>
        new(WhatsAppSendDisposition.DefinitelyRejected, null, errorCategory, errorCode, retryAfter, providerTimestamp);

    public static WhatsAppSendResult Ambiguous(
        string errorCategory = "UnknownOutcome",
        string? errorCode = null,
        TimeSpan? retryAfter = null,
        string? providerMessageId = null,
        DateTimeOffset? providerTimestamp = null) =>
        new(WhatsAppSendDisposition.Ambiguous, providerMessageId, errorCategory, errorCode, retryAfter, providerTimestamp);

    public override string ToString() =>
        $"WhatsAppSendResult(Disposition={Disposition}, HasProviderMessageId={!string.IsNullOrWhiteSpace(ProviderMessageId)}, ErrorCategory={ErrorCategory ?? "none"}, ErrorCode={ErrorCode ?? "none"}, RetryAfter={RetryAfter?.ToString() ?? "none"})";

    private static void ValidateRetryAfter(TimeSpan? retryAfter)
    {
        if (retryAfter.HasValue && retryAfter.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryAfter), "RetryAfter cannot be negative.");
    }
}

/// <summary>
/// Immutable logical outbound snapshot passed to a provider. The logical key is
/// an application idempotency/correlation identity; this contract does not claim
/// that an external provider delivers it exactly once.
/// </summary>
public sealed record WhatsAppOutboundRequest
{
    public string LogicalMessageKey { get; }
    public WhatsAppProviderAccountBinding AccountBinding { get; }
    public WhatsAppProviderAccountBinding ProviderAccount => AccountBinding;
    public string BusinessEndpointKey => AccountBinding.BusinessEndpointKey;
    public WhatsAppDestination Destination { get; }
    public WhatsAppOutboundContent Content { get; }
    public WhatsAppOutboundContent ContentSnapshot => Content;
    public string CorrelationId { get; }

    public WhatsAppOutboundRequest(
        string logicalMessageKey,
        WhatsAppProviderAccountBinding accountBinding,
        WhatsAppDestination destination,
        WhatsAppOutboundContent content,
        string correlationId)
    {
        WhatsAppContractValidation.RequiredReference(logicalMessageKey, nameof(logicalMessageKey));
        ArgumentNullException.ThrowIfNull(accountBinding);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(content);
        WhatsAppContractValidation.RequiredReference(correlationId, nameof(correlationId));

        LogicalMessageKey = logicalMessageKey;
        AccountBinding = accountBinding;
        Destination = destination;
        Content = content;
        CorrelationId = correlationId;
    }

    public WhatsAppOutboundRequest(
        WhatsAppProviderAccountBinding accountBinding,
        string logicalMessageKey,
        WhatsAppDestination destination,
        WhatsAppOutboundContent content,
        string correlationId)
        : this(logicalMessageKey, accountBinding, destination, content, correlationId)
    {
    }

    public override string ToString() =>
        $"WhatsAppOutboundRequest(LogicalMessageKey=[redacted], BusinessEndpointKey=[redacted], Destination={Destination.Kind}, Content={Content.Kind}, CorrelationId=[redacted])";
}

/// <summary>
/// Provider-neutral runtime boundary. Implementations report account-specific
/// capabilities and return one result per send attempt; retry loops belong to a
/// later outbox layer.
/// </summary>
public interface IWhatsAppProvider
{
    Task<WhatsAppProviderCapabilities> GetCapabilitiesAsync(
        WhatsAppProviderAccountBinding accountBinding,
        CancellationToken cancellationToken = default);

    Task<WhatsAppSendResult> SendAsync(
        WhatsAppOutboundRequest request,
        CancellationToken cancellationToken = default);
}

internal static class WhatsAppContractValidation
{
    public static void RequiredReference(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-blank provider-neutral reference is required.", parameterName);

        ValidateReferenceCharacters(value, parameterName);
    }

    public static void OptionalReference(string? value, string parameterName)
    {
        if (value is null)
            return;

        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("An optional provider-neutral reference cannot be blank.", parameterName);

        ValidateReferenceCharacters(value, parameterName);
    }

    private static void ValidateReferenceCharacters(string value, string parameterName)
    {
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("Provider-neutral references cannot contain line breaks.", parameterName);
    }
}
