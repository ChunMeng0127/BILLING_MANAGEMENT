namespace BillingControl.Storage.SharePoint;

/// <summary>
/// Configuration for the Microsoft Graph SharePoint document-storage adapter.
/// </summary>
/// <remarks>
/// Bind this type from <c>DocumentStorage:SharePoint</c>. The corresponding
/// environment-variable prefix is <c>DocumentStorage__SharePoint__</c>.
/// Values identifying the site, drive, folder, and metadata list are explicit
/// resource identifiers; the adapter never discovers them from a site path.
/// </remarks>
public sealed class SharePointDocumentStorageOptions
{
    public const string SectionName = "DocumentStorage:SharePoint";

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    public string SiteId { get; set; } = string.Empty;

    public string DriveId { get; set; } = string.Empty;

    /// <summary>
    /// The drive item ID of the pre-provisioned Intake folder.
    /// </summary>
    public string RootFolderItemId { get; set; } = string.Empty;

    /// <summary>
    /// The SharePoint document-library list ID associated with <see cref="DriveId"/>.
    /// </summary>
    public string MetadataListId { get; set; } = string.Empty;

    /// <summary>
    /// Internal name of the dedicated multiple-lines-of-text SharePoint column used
    /// for the provider-neutral immutable metadata envelope.
    /// </summary>
    public string MetadataFieldInternalName { get; set; } = "BillingControlMetadata";

    /// <summary>
    /// Client-credential settings used only by the optional built-in token provider.
    /// A caller may instead register an <see cref="IGraphAccessTokenProvider"/> that
    /// obtains a certificate or managed-identity token without putting a secret here.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Files at or below this size use the single-request upload endpoint. Set to
    /// zero to use upload sessions for every new object.
    /// </summary>
    public long ResumableUploadThresholdBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Upload-session fragment size. Graph requires a 320 KiB multiple and a value
    /// below the documented 60 MiB per-request limit.
    /// </summary>
    public int UploadChunkSizeBytes { get; set; } = 32 * 320 * 1024;

    public int MaxAttempts { get; set; } = 4;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public int RetryJitterMilliseconds { get; set; } = 250;

    public int MaxRetryDelayMilliseconds { get; set; } = 30_000;

    /// <summary>
    /// Maximum UTF-8 size of the JSON stored in the SharePoint metadata column.
    /// </summary>
    public int MetadataPayloadMaxBytes { get; set; } = 60_000;

    public void Validate()
    {
        if (!Uri.TryCreate(GraphBaseUrl, UriKind.Absolute, out var graphBaseUri) ||
            graphBaseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(graphBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(graphBaseUri.Query) ||
            !string.IsNullOrEmpty(graphBaseUri.Fragment))
        {
            throw new DocumentStorageValidationException(
                "GraphBaseUrl must be an absolute HTTPS URL without credentials or query parameters.");
        }

        RequireResourceId(SiteId, nameof(SiteId));
        RequireResourceId(DriveId, nameof(DriveId));
        RequireResourceId(RootFolderItemId, nameof(RootFolderItemId));
        RequireResourceId(MetadataListId, nameof(MetadataListId));
        RequireResourceId(MetadataFieldInternalName, nameof(MetadataFieldInternalName));

        if (ResumableUploadThresholdBytes < 0)
        {
            throw new DocumentStorageValidationException(
                "ResumableUploadThresholdBytes cannot be negative.");
        }

        const int graphChunkMultiple = 320 * 1024;
        const int graphMaximumChunkSize = 60 * 1024 * 1024;
        if (UploadChunkSizeBytes <= 0 ||
            UploadChunkSizeBytes >= graphMaximumChunkSize ||
            UploadChunkSizeBytes % graphChunkMultiple != 0)
        {
            throw new DocumentStorageValidationException(
                "UploadChunkSizeBytes must be a positive multiple of 320 KiB below 60 MiB.");
        }

        if (MaxAttempts <= 0 || MaxAttempts > 10)
        {
            throw new DocumentStorageValidationException("MaxAttempts must be between 1 and 10.");
        }

        if (RetryBaseDelayMilliseconds < 0 ||
            RetryJitterMilliseconds < 0 ||
            MaxRetryDelayMilliseconds < 0)
        {
            throw new DocumentStorageValidationException("Retry delay settings cannot be negative.");
        }

        if (MetadataPayloadMaxBytes <= 0)
        {
            throw new DocumentStorageValidationException("MetadataPayloadMaxBytes must be positive.");
        }
    }

    private static void RequireResourceId(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DocumentStorageValidationException($"{name} is required.");
        }
    }
}
