namespace BillingControl.Services.WhatsApp;

/// <summary>
/// Server-side configuration for one Meta WhatsApp Cloud API sender binding.
/// Values are intended to come from the normal ASP.NET Core configuration
/// pipeline; credentials and identifiers are deliberately not given defaults.
/// </summary>
public sealed class MetaWhatsAppOptions
{
    public const string SectionName = "MetaWhatsApp";

    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string BusinessAccountId { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BusinessEndpointKey { get; set; } = string.Empty;

    // Capability switches are account/runtime configuration, not domain facts.
    // Group and template support fail closed until explicitly enabled for the
    // configured account and verified against its current Meta eligibility.
    public bool SupportsDirectMessaging { get; set; } = true;
    public bool SupportsGroupMessaging { get; set; }
    public bool SupportsText { get; set; } = true;
    public bool SupportsTemplateMessaging { get; set; }
    public bool SupportsMedia { get; set; }
    public bool SupportsDocuments { get; set; }

    public WhatsAppProviderCapabilities GetCapabilities() => new(
        supportsDirectMessaging: SupportsDirectMessaging,
        supportsGroupMessaging: SupportsGroupMessaging,
        supportsText: SupportsText,
        supportsTemplateMessaging: SupportsTemplateMessaging,
        supportsMedia: SupportsMedia,
        supportsDocuments: SupportsDocuments);

    /// <summary>
    /// Returns a safe configuration code without returning any configured value.
    /// </summary>
    internal string? GetConfigurationErrorCode()
    {
        if (!IsSafeReference(GraphBaseUrl) ||
            !Uri.TryCreate(GraphBaseUrl.Trim(), UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
            return "InvalidGraphBaseUrl";

        var apiVersion = ApiVersion?.Trim('/') ?? string.Empty;
        if (!IsSafeReference(apiVersion) || apiVersion.Any(character => character is '/' or '\\' or '?' or '#'))
            return "MissingOrInvalidApiVersion";

        if (!IsSafeReference(AccessToken) || AccessToken.Any(char.IsWhiteSpace))
            return "MissingOrInvalidAccessToken";

        if (!IsSafeReference(BusinessAccountId))
            return "MissingBusinessAccountId";

        if (!IsSafeReference(PhoneNumberId))
            return "MissingPhoneNumberId";

        if (!IsSafeReference(BusinessEndpointKey))
            return "MissingBusinessEndpointKey";

        return null;
    }

    internal bool MatchesAccount(WhatsAppProviderAccountBinding accountBinding)
    {
        if (!string.Equals(BusinessEndpointKey, accountBinding.BusinessEndpointKey, StringComparison.Ordinal))
            return false;

        return accountBinding.ProviderAccountReference is null ||
               string.Equals(BusinessAccountId, accountBinding.ProviderAccountReference, StringComparison.Ordinal);
    }

    public override string ToString() =>
        $"MetaWhatsAppOptions(Configured={GetConfigurationErrorCode() is null}, " +
        $"Capabilities={GetCapabilities()})";

    private static bool IsSafeReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains('\r') &&
        !value.Contains('\n');
}
