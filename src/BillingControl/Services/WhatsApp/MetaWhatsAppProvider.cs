using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BillingControl.Services.WhatsApp;

/// <summary>
/// Meta WhatsApp Business Platform / Cloud API transport adapter.
///
/// This class performs one HTTP attempt per <see cref="SendAsync"/> call. It
/// deliberately contains no idempotency store, persistence, retry loop,
/// webhook handling, or business-state mutation.
/// </summary>
public sealed class MetaWhatsAppProvider : IWhatsAppProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MetaWhatsAppOptions _options;
    private readonly TimeProvider _timeProvider;

    public MetaWhatsAppProvider(
        HttpClient httpClient,
        IOptions<MetaWhatsAppOptions> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Value ?? throw new ArgumentException("Meta WhatsApp options are required.", nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<WhatsAppProviderCapabilities> GetCapabilitiesAsync(
        WhatsAppProviderAccountBinding accountBinding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountBinding);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_options.GetCapabilities());
    }

    public async Task<WhatsAppSendResult> SendAsync(
        WhatsAppOutboundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var configurationErrorCode = _options.GetConfigurationErrorCode();
        if (configurationErrorCode is not null)
            return WhatsAppSendResult.DefinitelyRejected("Configuration", configurationErrorCode);

        if (!_options.MatchesAccount(request.AccountBinding))
            return WhatsAppSendResult.DefinitelyRejected("Configuration", "AccountBindingMismatch");

        var capabilities = _options.GetCapabilities();
        var capabilityFailure = ValidateCapabilities(request, capabilities);
        if (capabilityFailure is not null)
            return capabilityFailure;

        Uri messagesEndpoint;
        object payload;
        try
        {
            messagesEndpoint = CreateMessagesEndpoint();
            payload = CreatePayload(request.Destination, request.Content);
        }
        catch (UriFormatException)
        {
            return WhatsAppSendResult.DefinitelyRejected("Configuration", "InvalidMessagesEndpoint");
        }
        catch (InvalidOperationException)
        {
            return WhatsAppSendResult.DefinitelyRejected("UnsupportedCapability", "UnsupportedContentKind");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, messagesEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken.Trim());

        try
        {
            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            var retryAfter = GetRetryAfter(response);
            if (response.IsSuccessStatusCode)
            {
                var success = await ReadSuccessResponseAsync(response.Content, cancellationToken).ConfigureAwait(false);
                return success.ProviderMessageId is not null
                    ? WhatsAppSendResult.Accepted(success.ProviderMessageId)
                    : WhatsAppSendResult.Ambiguous(
                        "MalformedProviderResponse",
                        success.ErrorCode,
                        retryAfter);
            }

            var providerErrorCode = await ReadProviderErrorCodeAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return MapHttpFailure(response.StatusCode, providerErrorCode, retryAfter);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout and a transport timeout do not prove whether
            // Meta accepted the request.
            return WhatsAppSendResult.Ambiguous("Timeout", "ClientTimeout");
        }
        catch (HttpRequestException)
        {
            // A transport failure after request transmission cannot prove the
            // provider outcome, so never convert it into a rejection.
            return WhatsAppSendResult.Ambiguous("Network", "HttpRequestException");
        }
        catch (IOException)
        {
            // A response-body transport failure is also not proof of delivery.
            return WhatsAppSendResult.Ambiguous("Network", "ResponseBodyReadFailure");
        }
    }

    private Uri CreateMessagesEndpoint()
    {
        var baseUri = new Uri(_options.GraphBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var apiVersion = Uri.EscapeDataString(_options.ApiVersion.Trim('/'));
        var phoneNumberId = Uri.EscapeDataString(_options.PhoneNumberId.Trim());
        return new Uri(baseUri, $"{apiVersion}/{phoneNumberId}/messages");
    }

    private static string GetRecipient(WhatsAppDestination destination) =>
        destination.Kind == WhatsAppDestinationKind.Direct
            ? destination.ProviderRecipientKey ?? destination.NormalizedE164 ?? destination.ProviderDestinationKey
            : destination.ProviderDestinationKey;

    private static object CreatePayload(WhatsAppDestination destination, WhatsAppOutboundContent content)
    {
        var recipient = GetRecipient(destination);
        var recipientType = destination.Kind switch
        {
            WhatsAppDestinationKind.Direct => "individual",
            WhatsAppDestinationKind.Group => "group",
            _ => throw new InvalidOperationException("Unsupported WhatsApp destination kind.")
        };

        return content switch
        {
            WhatsAppTextContent text => new Dictionary<string, object?>
            {
                ["messaging_product"] = "whatsapp",
                ["recipient_type"] = recipientType,
                ["to"] = recipient,
                ["type"] = "text",
                ["text"] = new Dictionary<string, object?>
                {
                    ["body"] = text.Text,
                    ["preview_url"] = false
                }
            },
            WhatsAppTemplateContent template => CreateTemplatePayload(template, recipient, recipientType),
            _ => throw new InvalidOperationException("Unsupported WhatsApp outbound content.")
        };
    }

    private static object CreateTemplatePayload(WhatsAppTemplateContent content, string recipient, string recipientType)
    {
        var template = new Dictionary<string, object?>
        {
            ["name"] = content.TemplateName,
            ["language"] = new Dictionary<string, object?>
            {
                ["code"] = content.Language
            }
        };

        if (content.Parameters.Count > 0)
        {
            template["components"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "body",
                    ["parameters"] = content.Parameters.Select(parameter => new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = parameter
                    }).ToArray()
                }
            };
        }

        return new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = recipientType,
            ["to"] = recipient,
            ["type"] = "template",
            ["template"] = template
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
            WhatsAppOutboundContentKind.Text or WhatsAppOutboundContentKind.Template => null,
            _ => WhatsAppSendResult.DefinitelyRejected("UnsupportedCapability", "UnsupportedContentKind")
        };
    }

    private async Task<MetaSuccessParseResult> ReadSuccessResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var response = await JsonSerializer.DeserializeAsync<MetaSuccessEnvelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            var messageId = response?.Messages?
                .Select(message => message.Id?.Trim())
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

            return messageId is not null
                ? new MetaSuccessParseResult(messageId)
                : new MetaSuccessParseResult(null, response?.Messages is null ? "MissingMessages" : "MissingMessageId");
        }
        catch (JsonException)
        {
            return new MetaSuccessParseResult(null, "InvalidJson");
        }
        catch (NotSupportedException)
        {
            return new MetaSuccessParseResult(null, "UnsupportedResponseContent");
        }
        catch (InvalidOperationException)
        {
            return new MetaSuccessParseResult(null, "InvalidResponseContent");
        }
    }

    private static async Task<string?> ReadProviderErrorCodeAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var response = await JsonSerializer.DeserializeAsync<MetaErrorResponse>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            var code = response?.Error?.Code;
            if (code is null)
                return null;

            if (code.Value.ValueKind == JsonValueKind.Number && code.Value.TryGetInt64(out var numericCode))
                return numericCode.ToString(CultureInfo.InvariantCulture);

            if (code.Value.ValueKind == JsonValueKind.String)
            {
                var stringCode = code.Value.GetString();
                if (!string.IsNullOrWhiteSpace(stringCode) &&
                    stringCode.Length <= 64 &&
                    !stringCode.Contains('\r') &&
                    !stringCode.Contains('\n'))
                    return stringCode;
            }
        }
        catch (JsonException)
        {
            // The HTTP status still provides a conservative classification.
        }
        catch (NotSupportedException)
        {
            // The HTTP status still provides a conservative classification.
        }
        catch (InvalidOperationException)
        {
            // The HTTP status still provides a conservative classification.
        }
        catch (IOException)
        {
            // The HTTP status still provides a conservative classification.
        }
        catch (HttpRequestException)
        {
            // The HTTP status still provides a conservative classification.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The HTTP status still provides a conservative classification.
        }

        return null;
    }

    private static WhatsAppSendResult MapHttpFailure(
        HttpStatusCode statusCode,
        string? providerErrorCode,
        TimeSpan? retryAfter)
    {
        var httpCode = $"HTTP_{(int)statusCode}";
        var errorCode = providerErrorCode ?? httpCode;

        return statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                WhatsAppSendResult.DefinitelyRejected("Authentication", errorCode, retryAfter),
            HttpStatusCode.Forbidden =>
                WhatsAppSendResult.DefinitelyRejected("Authorization", errorCode, retryAfter),
            HttpStatusCode.BadRequest =>
                WhatsAppSendResult.DefinitelyRejected("ProviderValidation", errorCode, retryAfter),
            HttpStatusCode.TooManyRequests =>
                WhatsAppSendResult.DefinitelyRejected("RateLimited", errorCode, retryAfter),
            HttpStatusCode.RequestTimeout =>
                WhatsAppSendResult.Ambiguous("Timeout", errorCode, retryAfter),
            _ when (int)statusCode >= 500 =>
                WhatsAppSendResult.Ambiguous("ProviderUnavailable", errorCode, retryAfter),
            _ when (int)statusCode >= 400 =>
                WhatsAppSendResult.DefinitelyRejected("ProviderRejected", errorCode, retryAfter),
            _ =>
                WhatsAppSendResult.Ambiguous("UnexpectedHttpStatus", errorCode, retryAfter)
        };
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        if (retryAfter.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;

        if (retryAfter.Date is { } date)
        {
            try
            {
                var delay = date - _timeProvider.GetUtcNow();
                return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private sealed class MetaSuccessEnvelope
    {
        [JsonPropertyName("messages")]
        public List<MetaMessage>? Messages { get; set; }
    }

    private sealed record MetaSuccessParseResult(string? ProviderMessageId, string? ErrorCode = null);

    private sealed class MetaMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    private sealed class MetaErrorResponse
    {
        [JsonPropertyName("error")]
        public MetaError? Error { get; set; }
    }

    private sealed class MetaError
    {
        [JsonPropertyName("code")]
        public JsonElement? Code { get; set; }
    }
}
