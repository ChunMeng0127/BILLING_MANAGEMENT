using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BillingControl.Storage.SharePoint;

/// <summary>
/// Microsoft Graph v1.0 adapter for an explicitly selected SharePoint document library.
/// </summary>
/// <remarks>
/// The drive and intake-folder item IDs are configured explicitly. A stable, non-sensitive
/// filename derived from the logical key is used only as an idempotency locator; the
/// returned DriveId plus ItemId remains the canonical storage identity. The adapter never
/// calls Graph sharing-link or permissions APIs.
/// </remarks>
public sealed class GraphSharePointDocumentStorage : IDocumentStorage
{
    private const string ProviderName = "MicrosoftGraphSharePoint";
    private const int HashBufferSize = 80 * 1024;
    private const string StableFilePrefix = "billing-control-artifact-";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly HttpClient httpClient;
    private readonly IGraphAccessTokenProvider accessTokenProvider;
    private readonly IOptions<SharePointDocumentStorageOptions> options;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;

    public GraphSharePointDocumentStorage(
        HttpClient httpClient,
        IGraphAccessTokenProvider accessTokenProvider,
        IOptions<SharePointDocumentStorageOptions> options,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.accessTokenProvider = accessTokenProvider
            ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.options.Value.Validate();
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    public async Task<DocumentStorageReference> StoreImmutableAsync(
        DocumentStorageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var configured = GetOptions();
        EnsureRequestCanBeUploaded(request);
        var serializedMetadata = SerializeMetadata(request, configured);
        await using var spool = await SpoolAndValidateAsync(request, cancellationToken);

        var stableFileName = CreateStableFileName(request.LogicalStorageKey);
        var existing = await FindChildByNameAsync(stableFileName, cancellationToken);
        if (existing is not null)
        {
            return await ReconcileExistingAsync(
                existing,
                request,
                serializedMetadata,
                cancellationToken);
        }

        GraphDriveItem uploaded;
        try
        {
            uploaded = request.ByteLength <= configured.ResumableUploadThresholdBytes
                ? await UploadSmallAsync(
                    stableFileName,
                    request,
                    spool,
                    cancellationToken)
                : await UploadResumableAsync(
                    stableFileName,
                    request,
                    spool,
                    cancellationToken);
        }
        catch (GraphStorageOperationException exception) when (exception.MayHaveBeenAccepted)
        {
            // A timeout can occur after Graph accepted the immutable object. Reconcile
            // the deterministic locator before allowing a caller to retry the operation.
            var reconciled = await FindChildByNameAsync(stableFileName, cancellationToken);
            if (reconciled is not null)
            {
                return await ReconcileExistingAsync(
                    reconciled,
                    request,
                    serializedMetadata,
                    cancellationToken);
            }

            throw;
        }

        var hydrated = await GetDriveItemByIdAsync(
            RequireItemId(uploaded),
            includeListItem: true,
            cancellationToken);
        if (hydrated is null)
        {
            throw new GraphStorageOperationException(
                "The uploaded Graph item could not be read back.",
                mayHaveBeenAccepted: true);
        }

        EnsureUploadedItemMatchesRequest(hydrated, request);
        await WriteMetadataAsync(hydrated, serializedMetadata, cancellationToken);

        var finalItem = await GetDriveItemByIdAsync(
            RequireItemId(hydrated),
            includeListItem: false,
            cancellationToken);
        if (finalItem is null)
        {
            throw new GraphStorageOperationException(
                "The stored Graph item disappeared during metadata finalization.",
                mayHaveBeenAccepted: true);
        }

        EnsureUploadedItemMatchesRequest(finalItem, request);
        return ToReference(finalItem);
    }

    public async Task<Stream?> OpenReadAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BelongsToThisProvider(reference))
        {
            return null;
        }

        var response = await SendGraphRequestAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildGraphUri($"drives/{Escape(reference.ContainerId)}/items/{Escape(reference.ObjectId)}/content")),
            includeBearerToken: true,
            mayHaveBeenAccepted: false,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null ||
                !location.IsAbsoluteUri ||
                location.Scheme != Uri.UriSchemeHttps)
            {
                throw new DocumentStorageValidationException(
                    "Microsoft Graph returned an invalid content redirect.");
            }

            // Graph content downloads can redirect to a pre-authenticated URL. Never
            // attach the Graph bearer token to that URL.
            response = await SendGraphRequestAsync(
                () => new HttpRequestMessage(HttpMethod.Get, location),
                includeBearerToken: false,
                mayHaveBeenAccepted: false,
                cancellationToken);
        }

        try
        {
            EnsureSuccess(response);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new GraphResponseStream(content, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<DocumentStorageMetadata?> GetMetadataAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BelongsToThisProvider(reference))
        {
            return null;
        }

        var item = await GetDriveItemByIdAsync(
            reference.ObjectId,
            includeListItem: true,
            cancellationToken);
        if (item is null)
        {
            return null;
        }

        var metadata = await ReadMetadataEnvelopeAsync(item, cancellationToken);
        return ToMetadata(item, metadata);
    }

    public async Task<DocumentStorageVerificationResult> VerifyAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BelongsToThisProvider(reference))
        {
            return new DocumentStorageVerificationResult(
                DocumentStorageVerificationStatus.Missing,
                reference,
                "The reference belongs to another storage provider or container.");
        }

        var item = await GetDriveItemByIdAsync(
            reference.ObjectId,
            includeListItem: false,
            cancellationToken);
        if (item is null)
        {
            return new DocumentStorageVerificationResult(
                DocumentStorageVerificationStatus.Missing,
                reference,
                "The Graph item is not present in the configured drive.");
        }

        var current = ToReference(item);
        if (reference.ETag is not null &&
            !string.Equals(reference.ETag, current.ETag, StringComparison.Ordinal))
        {
            return new DocumentStorageVerificationResult(
                DocumentStorageVerificationStatus.MetadataMismatch,
                current,
                "The Graph item version tag changed after the reference was stored.");
        }

        return new DocumentStorageVerificationResult(
            DocumentStorageVerificationStatus.Exists,
            current);
    }

    private async Task<GraphDriveItem> UploadSmallAsync(
        string stableFileName,
        DocumentStorageRequest request,
        SpoolFile spool,
        CancellationToken cancellationToken)
    {
        var uri = BuildGraphUri(
            $"drives/{Escape(requestContainerId())}/items/{Escape(requestRootFolderId())}:/{Escape(stableFileName)}:/content");

        using var response = await SendGraphRequestAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Put, uri);
                message.Headers.TryAddWithoutValidation("If-None-Match", "*");
                var content = new StreamContent(spool.OpenRead());
                content.Headers.ContentType = new MediaTypeHeaderValue(request.MimeType);
                content.Headers.ContentLength = spool.Length;
                message.Content = content;
                return message;
            },
            includeBearerToken: true,
            mayHaveBeenAccepted: true,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            throw new GraphStorageOperationException(
                "The immutable Graph object already exists.",
                mayHaveBeenAccepted: true);
        }

        EnsureSuccess(response);
        return await ReadDriveItemAsync(response, cancellationToken);
    }

    private async Task<GraphDriveItem> UploadResumableAsync(
        string stableFileName,
        DocumentStorageRequest request,
        SpoolFile spool,
        CancellationToken cancellationToken)
    {
        // Graph upload sessions are temporary. If a session disappears before
        // completion, start a fresh session; never turn the temporary URL into a
        // permanent identity or blindly replace a completed item.
        for (var sessionAttempt = 1; sessionAttempt <= 2; sessionAttempt++)
        {
            try
            {
                return await UploadResumableSessionAsync(
                    stableFileName,
                    request,
                    spool,
                    cancellationToken);
            }
            catch (GraphUploadSessionExpiredException) when (sessionAttempt < 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        throw new GraphStorageOperationException(
            "The Graph upload session expired before completion.",
            mayHaveBeenAccepted: true);
    }

    private async Task<GraphDriveItem> UploadResumableSessionAsync(
        string stableFileName,
        DocumentStorageRequest request,
        SpoolFile spool,
        CancellationToken cancellationToken)
    {
        var configured = GetOptions();
        var sessionUri = BuildGraphUri(
            $"drives/{Escape(requestContainerId())}/items/{Escape(requestRootFolderId())}:/{Escape(stableFileName)}:/createUploadSession");
        var sessionBody = new
        {
            item = new Dictionary<string, object>
            {
                ["@microsoft.graph.conflictBehavior"] = "fail",
                ["name"] = stableFileName
            }
        };
        var serializedBody = JsonSerializer.Serialize(sessionBody, JsonOptions);

        using var sessionResponse = await SendGraphRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Post, sessionUri)
            {
                Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
            },
            includeBearerToken: true,
            mayHaveBeenAccepted: false,
            cancellationToken);

        if (sessionResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            throw new GraphStorageOperationException(
                "The immutable Graph object already exists.",
                mayHaveBeenAccepted: true);
        }

        EnsureSuccess(sessionResponse);
        var session = await ReadUploadSessionAsync(sessionResponse, cancellationToken);
        var uploadUrl = ValidateUploadUrl(session.UploadUrl!);
        var nextOffset = 0L;
        await using var input = spool.OpenRead();

        while (nextOffset < request.ByteLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            input.Position = nextOffset;
            var remaining = request.ByteLength - nextOffset;
            var chunkLength = checked((int)Math.Min(remaining, configured.UploadChunkSizeBytes));
            var chunk = ArrayPool<byte>.Shared.Rent(chunkLength);
            try
            {
                await ReadExactlyAsync(input, chunk.AsMemory(0, chunkLength), cancellationToken);
                var chunkStart = nextOffset;
                var chunkEnd = checked(chunkStart + chunkLength - 1);

                using var chunkResponse = await SendGraphRequestAsync(
                    () =>
                    {
                        var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
                        var content = new ByteArrayContent(chunk, 0, chunkLength);
                        content.Headers.ContentLength = chunkLength;
                        content.Headers.ContentRange = new ContentRangeHeaderValue(
                            chunkStart,
                            chunkEnd,
                            request.ByteLength);
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        message.Content = content;
                        // The upload URL is pre-authenticated. Deliberately no bearer token.
                        return message;
                    },
                    includeBearerToken: false,
                    mayHaveBeenAccepted: true,
                    cancellationToken);

                if (chunkResponse.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
                {
                    throw new GraphStorageOperationException(
                        "The immutable Graph object already exists.",
                        mayHaveBeenAccepted: true);
                }

                if (chunkResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new GraphUploadSessionExpiredException();
                }

                if (chunkResponse.StatusCode == HttpStatusCode.Accepted)
                {
                    var progress = await ReadUploadProgressAsync(chunkResponse, cancellationToken);
                    if (progress.NextExpectedRanges!.Count == 0)
                    {
                        nextOffset = request.ByteLength;
                    }
                    else
                    {
                        nextOffset = ParseNextExpectedOffset(progress.NextExpectedRanges);
                        if (nextOffset < 0 || nextOffset > request.ByteLength)
                        {
                            throw new DocumentStorageValidationException(
                                "Microsoft Graph returned an invalid upload-session range.");
                        }
                    }

                    continue;
                }

                EnsureSuccess(chunkResponse);
                return await ReadDriveItemAsync(chunkResponse, cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }

        // A SharePoint/OneDrive for Business session may require an explicit zero-byte
        // POST when the service returned 202 for the final fragment.
        using var completionResponse = await SendGraphRequestAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, uploadUrl)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                };
                message.Content.Headers.ContentLength = 0;
                return message;
            },
            includeBearerToken: false,
            mayHaveBeenAccepted: true,
            cancellationToken);
        if (completionResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GraphUploadSessionExpiredException();
        }

        EnsureSuccess(completionResponse);
        return await ReadDriveItemAsync(completionResponse, cancellationToken);
    }

    private async Task<DocumentStorageReference> ReconcileExistingAsync(
        GraphDriveItem existing,
        DocumentStorageRequest request,
        SerializedMetadata serializedMetadata,
        CancellationToken cancellationToken)
    {
        var item = await GetDriveItemByIdAsync(
            RequireItemId(existing),
            includeListItem: true,
            cancellationToken);
        if (item is null)
        {
            throw new GraphStorageOperationException(
                "The candidate immutable Graph item disappeared during reconciliation.",
                mayHaveBeenAccepted: true);
        }

        StorageMetadataEnvelope? existingMetadata;
        try
        {
            existingMetadata = await ReadMetadataEnvelopeAsync(item, cancellationToken);
        }
        catch (GraphMetadataMissingException)
        {
            // A prior attempt can have committed the binary before its metadata PATCH.
            // Repair only the missing metadata after proving the immutable bytes match.
            await EnsureRemoteContentMatchesAsync(item, request, cancellationToken);
            await WriteMetadataAsync(item, serializedMetadata, cancellationToken);
            var finalized = await GetDriveItemByIdAsync(
                RequireItemId(item),
                includeListItem: true,
                cancellationToken);
            if (finalized is null)
            {
                throw new GraphStorageOperationException(
                    "The reconciled Graph item disappeared during metadata finalization.",
                    mayHaveBeenAccepted: true);
            }

            existingMetadata = await ReadMetadataEnvelopeAsync(finalized, cancellationToken);
            item = finalized;
        }

        EnsureSameImmutableMetadata(existingMetadata, request);
        await EnsureRemoteContentMatchesAsync(item, request, cancellationToken);
        return ToReference(item);
    }

    private async Task<GraphDriveItem?> FindChildByNameAsync(
        string stableFileName,
        CancellationToken cancellationToken)
    {
        var configured = GetOptions();
        var nextUri = BuildGraphUri(
            $"drives/{Escape(configured.DriveId)}/items/{Escape(configured.RootFolderItemId)}/children" +
            "?$select=id,name,size,eTag,webUrl,createdDateTime,sharepointIds,file&$top=200");

        while (nextUri is not null)
        {
            using var response = await SendGraphRequestAsync(
                () => new HttpRequestMessage(HttpMethod.Get, nextUri),
                includeBearerToken: true,
                mayHaveBeenAccepted: false,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new DocumentStorageException(
                    "The configured SharePoint intake folder was not found.");
            }

            EnsureSuccess(response);
            var page = await ReadChildrenPageAsync(response, cancellationToken);
            foreach (var item in page.Items!)
            {
                if (string.Equals(item.Name, stableFileName, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            nextUri = page.NextLink is null
                ? null
                : ValidateGraphNextLink(page.NextLink);
        }

        return null;
    }

    private async Task<GraphDriveItem?> GetDriveItemByIdAsync(
        string objectId,
        bool includeListItem,
        CancellationToken cancellationToken)
    {
        var select = "id,name,size,eTag,webUrl,createdDateTime,sharepointIds,file";
        var expand = includeListItem ? "&$expand=listItem" : string.Empty;
        var uri = BuildGraphUri(
            $"drives/{Escape(requestContainerId())}/items/{Escape(objectId)}?" +
            $"$select={select}{expand}");
        using var response = await SendGraphRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            includeBearerToken: true,
            mayHaveBeenAccepted: false,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);
        return await ReadDriveItemAsync(response, cancellationToken);
    }

    private async Task WriteMetadataAsync(
        GraphDriveItem item,
        SerializedMetadata serializedMetadata,
        CancellationToken cancellationToken)
    {
        var listItemId = RequireListItemId(item);
        var configured = GetOptions();
        var uri = BuildGraphUri(
            $"sites/{Escape(configured.SiteId)}/lists/{Escape(configured.MetadataListId)}/" +
            $"items/{Escape(listItemId)}/fields");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [configured.MetadataFieldInternalName] = serializedMetadata.Json
        };
        var body = JsonSerializer.Serialize(fields, JsonOptions);

        using var response = await SendGraphRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            },
            includeBearerToken: true,
            mayHaveBeenAccepted: true,
            cancellationToken);
        EnsureSuccess(response);
    }

    private async Task<StorageMetadataEnvelope> ReadMetadataEnvelopeAsync(
        GraphDriveItem item,
        CancellationToken cancellationToken)
    {
        var listItemId = RequireListItemId(item);
        var configured = GetOptions();
        var uri = BuildGraphUri(
            $"sites/{Escape(configured.SiteId)}/lists/{Escape(configured.MetadataListId)}/" +
            $"items/{Escape(listItemId)}?expand=fields");
        using var response = await SendGraphRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            includeBearerToken: true,
            mayHaveBeenAccepted: false,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new DocumentStorageException(
                "The SharePoint metadata list item could not be found.");
        }

        EnsureSuccess(response);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned malformed SharePoint metadata.",
                exception);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("fields", out var fields) ||
                fields.ValueKind != JsonValueKind.Object ||
                !fields.TryGetProperty(configured.MetadataFieldInternalName, out var fieldValue) ||
                fieldValue.ValueKind == JsonValueKind.Null)
            {
                throw new GraphMetadataMissingException();
            }

            if (fieldValue.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(fieldValue.GetString()))
            {
                throw new DocumentStorageValidationException(
                    "The SharePoint metadata field has an invalid value.");
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<StorageMetadataEnvelope>(
                    fieldValue.GetString()!,
                    MetadataJsonOptions);
                if (envelope is null || envelope.Metadata is null)
                {
                    throw new DocumentStorageValidationException(
                        "The SharePoint metadata envelope is incomplete.");
                }

                return envelope;
            }
            catch (JsonException exception)
            {
                throw new DocumentStorageValidationException(
                    "The SharePoint metadata envelope is malformed.",
                    exception);
            }
        }
    }

    private async Task EnsureRemoteContentMatchesAsync(
        GraphDriveItem item,
        DocumentStorageRequest request,
        CancellationToken cancellationToken)
    {
        await using var content = await OpenReadCoreAsync(RequireItemId(item), cancellationToken)
            ?? throw new GraphStorageOperationException(
                "The candidate Graph item has no readable content.",
                mayHaveBeenAccepted: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        try
        {
            long length = 0;
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, HashBufferSize), cancellationToken)) > 0)
            {
                length = checked(length + read);
                hash.AppendData(buffer, 0, read);
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (length != request.ByteLength ||
                !string.Equals(actualHash, request.Sha256Hash, StringComparison.Ordinal))
            {
                throw new DocumentStorageConflictException(
                    request.LogicalStorageKey,
                    "The existing Graph object has different immutable content.");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<Stream?> OpenReadCoreAsync(
        string objectId,
        CancellationToken cancellationToken)
    {
        var response = await SendGraphRequestAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                BuildGraphUri($"drives/{Escape(requestContainerId())}/items/{Escape(objectId)}/content")),
            includeBearerToken: true,
            mayHaveBeenAccepted: false,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null ||
                !location.IsAbsoluteUri ||
                location.Scheme != Uri.UriSchemeHttps)
            {
                throw new DocumentStorageValidationException(
                    "Microsoft Graph returned an invalid content redirect.");
            }

            response = await SendGraphRequestAsync(
                () => new HttpRequestMessage(HttpMethod.Get, location),
                includeBearerToken: false,
                mayHaveBeenAccepted: false,
                cancellationToken);
        }

        try
        {
            EnsureSuccess(response);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new GraphResponseStream(content, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendGraphRequestAsync(
        Func<HttpRequestMessage> requestFactory,
        bool includeBearerToken,
        bool mayHaveBeenAccepted,
        CancellationToken cancellationToken)
    {
        var configured = GetOptions();
        Exception? lastTransportException = null;
        for (var attempt = 1; attempt <= configured.MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpRequestMessage? request = null;
            try
            {
                request = requestFactory();
                if (includeBearerToken)
                {
                    var token = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        throw new DocumentStorageValidationException(
                            "The Graph access-token provider returned an empty token.");
                    }

                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == configured.MaxAttempts)
                {
                    if (attempt == configured.MaxAttempts && IsTransient(response.StatusCode))
                    {
                        response.Dispose();
                        throw new GraphStorageOperationException(
                            "Microsoft Graph request failed after the retry policy.",
                            mayHaveBeenAccepted,
                            lastTransportException);
                    }

                    return response;
                }

                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                await delayAsync(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                lastTransportException = exception;
                if (attempt == configured.MaxAttempts)
                {
                    throw new GraphStorageOperationException(
                        "Microsoft Graph request could not be completed.",
                        mayHaveBeenAccepted,
                        exception);
                }

                await delayAsync(GetRetryDelay(null, attempt), cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastTransportException = exception;
                if (attempt == configured.MaxAttempts)
                {
                    throw new GraphStorageOperationException(
                        "Microsoft Graph request timed out.",
                        mayHaveBeenAccepted,
                        exception);
                }

                await delayAsync(GetRetryDelay(null, attempt), cancellationToken);
            }
            finally
            {
                request?.Dispose();
            }
        }

        throw new GraphStorageOperationException(
            "Microsoft Graph request could not be completed.",
            mayHaveBeenAccepted,
            lastTransportException);
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage? response, int failedAttempt)
    {
        if (response?.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter >= TimeSpan.Zero)
        {
            return retryAfter;
        }

        if (response?.Headers.RetryAfter?.Date is { } retryDate)
        {
            var dateDelay = retryDate - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay;
            }
        }

        var configured = GetOptions();
        var multiplier = Math.Pow(2, Math.Max(0, failedAttempt - 1));
        var exponentialMilliseconds = configured.RetryBaseDelayMilliseconds * multiplier;
        var boundedMilliseconds = Math.Min(
            configured.MaxRetryDelayMilliseconds,
            exponentialMilliseconds);
        var jitter = configured.RetryJitterMilliseconds == 0
            ? 0
            : Random.Shared.Next(0, configured.RetryJitterMilliseconds + 1);
        return TimeSpan.FromMilliseconds(Math.Max(0, boundedMilliseconds + jitter));
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
           statusCode == (HttpStatusCode)429 ||
           statusCode == HttpStatusCode.InternalServerError ||
           statusCode == HttpStatusCode.BadGateway ||
           statusCode == HttpStatusCode.ServiceUnavailable ||
           statusCode == HttpStatusCode.GatewayTimeout;

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new DocumentStorageException("Microsoft Graph authorization failed.");
        }

        throw new DocumentStorageException("Microsoft Graph storage operation failed.");
    }

    private async Task<GraphDriveItem> ReadDriveItemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var item = JsonSerializer.Deserialize<GraphDriveItem>(payload, JsonOptions);
            return item ?? throw new DocumentStorageValidationException(
                "Microsoft Graph returned an empty drive-item response.");
        }
        catch (JsonException exception)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned malformed drive-item metadata.",
                exception);
        }
    }

    private async Task<UploadSession> ReadUploadSessionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var session = JsonSerializer.Deserialize<UploadSession>(payload, JsonOptions);
            if (session is null || string.IsNullOrWhiteSpace(session.UploadUrl))
            {
                throw new DocumentStorageValidationException(
                    "Microsoft Graph returned an incomplete upload-session response.");
            }

            return session;
        }
        catch (JsonException exception)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned malformed upload-session metadata.",
                exception);
        }
    }

    private async Task<UploadProgress> ReadUploadProgressAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var progress = JsonSerializer.Deserialize<UploadProgress>(payload, JsonOptions);
            if (progress is null || progress.NextExpectedRanges is null)
            {
                throw new DocumentStorageValidationException(
                    "Microsoft Graph returned an incomplete upload-session progress response.");
            }

            return progress;
        }
        catch (JsonException exception)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned malformed upload-session progress.",
                exception);
        }
    }

    private async Task<ChildrenPage> ReadChildrenPageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var page = JsonSerializer.Deserialize<ChildrenPage>(payload, JsonOptions);
            if (page is null || page.Items is null)
            {
                throw new DocumentStorageValidationException(
                    "Microsoft Graph returned an incomplete children response.");
            }

            return page;
        }
        catch (JsonException exception)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned malformed children metadata.",
                exception);
        }
    }

    private static async Task ReadExactlyAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new DocumentStorageValidationException(
                    "The content stream ended before the declared byte length.");
            }

            offset += read;
        }
    }

    private async Task<SpoolFile> SpoolAndValidateAsync(
        DocumentStorageRequest request,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"billing-control-graph-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                HashBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
                try
                {
                    long length = 0;
                    int read;
                    while ((read = await request.Content.ReadAsync(
                               buffer.AsMemory(0, HashBufferSize),
                               cancellationToken)) > 0)
                    {
                        length = checked(length + read);
                        if (length > request.ByteLength)
                        {
                            throw new DocumentStorageValidationException(
                                "The content length exceeds the declared ByteLength.");
                        }

                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        hash.AppendData(buffer, 0, read);
                    }

                    if (length != request.ByteLength)
                    {
                        throw new DocumentStorageValidationException(
                            "The content length does not match ByteLength.");
                    }

                    var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                    if (!string.Equals(actualHash, request.Sha256Hash, StringComparison.Ordinal))
                    {
                        throw new DocumentStorageValidationException(
                            "The content SHA-256 does not match Sha256Hash.");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return new SpoolFile(path, request.ByteLength);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static SerializedMetadata SerializeMetadata(
        DocumentStorageRequest request,
        SharePointDocumentStorageOptions configured)
    {
        var envelope = new StorageMetadataEnvelope
        {
            LogicalStorageKey = request.LogicalStorageKey,
            OriginalFileName = request.OriginalFileName,
            MimeType = request.MimeType,
            ByteLength = request.ByteLength,
            Sha256Hash = request.Sha256Hash,
            ReceivedAt = request.ReceivedAt,
            Source = request.Source,
            SourceReference = request.SourceReference,
            Metadata = request.Metadata
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
        };
        var json = JsonSerializer.Serialize(envelope, MetadataJsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > configured.MetadataPayloadMaxBytes)
        {
            throw new DocumentStorageValidationException(
                "The immutable storage metadata exceeds the configured SharePoint field limit.");
        }

        return new SerializedMetadata(json);
    }

    private static void EnsureSameImmutableMetadata(
        StorageMetadataEnvelope existing,
        DocumentStorageRequest request)
    {
        if (!string.Equals(existing.LogicalStorageKey, request.LogicalStorageKey, StringComparison.Ordinal) ||
            !string.Equals(existing.OriginalFileName, request.OriginalFileName, StringComparison.Ordinal) ||
            !string.Equals(existing.MimeType, request.MimeType, StringComparison.Ordinal) ||
            existing.ByteLength != request.ByteLength ||
            !string.Equals(existing.Sha256Hash, request.Sha256Hash, StringComparison.OrdinalIgnoreCase) ||
            existing.ReceivedAt != request.ReceivedAt ||
            !string.Equals(existing.Source, request.Source, StringComparison.Ordinal) ||
            !string.Equals(existing.SourceReference, request.SourceReference, StringComparison.Ordinal) ||
            !MetadataEquals(existing.Metadata, request.Metadata))
        {
            throw new DocumentStorageConflictException(
                request.LogicalStorageKey,
                "The logical storage key was reused with conflicting immutable metadata.");
        }
    }

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in right)
        {
            if (!left.TryGetValue(entry.Key, out var value) ||
                !string.Equals(value, entry.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureUploadedItemMatchesRequest(
        GraphDriveItem item,
        DocumentStorageRequest request)
    {
        if (item.File is null ||
            item.Size is null ||
            item.Size.Value != request.ByteLength)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned an item whose file metadata does not match the upload.");
        }
    }

    private DocumentStorageMetadata ToMetadata(
        GraphDriveItem item,
        StorageMetadataEnvelope envelope)
    {
        if (item.Size is null || item.Size.Value != envelope.ByteLength)
        {
            throw new DocumentStorageValidationException(
                "The stored Graph item size does not match its immutable metadata.");
        }

        var reference = ToReference(item);
        return new DocumentStorageMetadata(
            reference,
            envelope.LogicalStorageKey ?? string.Empty,
            envelope.OriginalFileName ?? string.Empty,
            envelope.MimeType ?? string.Empty,
            envelope.ByteLength,
            envelope.Sha256Hash ?? string.Empty,
            envelope.ReceivedAt,
            envelope.Source,
            envelope.SourceReference,
            envelope.Metadata);
    }

    private DocumentStorageReference ToReference(GraphDriveItem item)
    {
        var objectId = RequireItemId(item);
        var configured = GetOptions();
        return new DocumentStorageReference(
            ProviderName,
            configured.DriveId,
            objectId,
            item.ETag,
            item.WebUrl,
            item.CreatedDateTime ?? DateTimeOffset.UtcNow);
    }

    private static string RequireItemId(GraphDriveItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned an item without an ID.");
        }

        return item.Id.Trim();
    }

    private static string RequireListItemId(GraphDriveItem item)
    {
        var listItemId = item.ListItem?.Id ?? item.SharePointIds?.ListItemId;
        if (string.IsNullOrWhiteSpace(listItemId))
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph did not return the SharePoint list-item ID required for metadata.");
        }

        return listItemId.Trim();
    }

    private static long ParseNextExpectedOffset(IReadOnlyList<string> ranges)
    {
        var first = ranges
            .Where(range => !string.IsNullOrWhiteSpace(range))
            .Select(range => range.Split('-', 2)[0])
            .FirstOrDefault();
        if (first is null ||
            !long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            throw new DocumentStorageValidationException(
            "Microsoft Graph returned an empty upload-session range.");
        }

        return offset;
    }

    private Uri ValidateUploadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned an invalid upload URL.");
        }

        return uri;
    }

    private Uri ValidateGraphNextLink(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, BuildGraphUri("$").Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentStorageValidationException(
                "Microsoft Graph returned an invalid pagination URL.");
        }

        return uri;
    }

    private Uri BuildGraphUri(string relativePath)
    {
        var baseUrl = GetOptions().GraphBaseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), relativePath.TrimStart('/'));
    }

    private static string Escape(string value) => Uri.EscapeDataString(value.Trim());

    private static string CreateStableFileName(string logicalStorageKey)
    {
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(logicalStorageKey)))
            .ToLowerInvariant();
        return $"{StableFilePrefix}{hash}.bin";
    }

    private bool BelongsToThisProvider(DocumentStorageReference reference)
    {
        var configured = GetOptions();
        return string.Equals(reference.Provider, ProviderName, StringComparison.Ordinal) &&
               string.Equals(reference.ContainerId, configured.DriveId, StringComparison.Ordinal);
    }

    private SharePointDocumentStorageOptions GetOptions()
    {
        var configured = options.Value;
        configured.Validate();
        return configured;
    }

    private static void EnsureRequestCanBeUploaded(DocumentStorageRequest request)
    {
        if (!request.Content.CanRead)
        {
            throw new DocumentStorageValidationException("The content stream must be readable.");
        }

        if (!MediaTypeHeaderValue.TryParse(request.MimeType, out _))
        {
            throw new DocumentStorageValidationException("MimeType is not a valid media type.");
        }
    }

    private string requestContainerId() => GetOptions().DriveId;

    private string requestRootFolderId() => GetOptions().RootFolderItemId;

    private sealed record SerializedMetadata(string Json);

    private sealed class StorageMetadataEnvelope
    {
        public string? LogicalStorageKey { get; set; }

        public string? OriginalFileName { get; set; }

        public string? MimeType { get; set; }

        public long ByteLength { get; set; }

        public string? Sha256Hash { get; set; }

        public DateTimeOffset? ReceivedAt { get; set; }

        public string? Source { get; set; }

        public string? SourceReference { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }
    }

    private sealed class GraphDriveItem
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public long? Size { get; set; }

        public string? ETag { get; set; }

        public string? WebUrl { get; set; }

        public DateTimeOffset? CreatedDateTime { get; set; }

        public GraphFileFacet? File { get; set; }

        public GraphSharePointIds? SharePointIds { get; set; }

        public GraphListItem? ListItem { get; set; }

    }

    private sealed class GraphFileFacet
    {
        public string? MimeType { get; set; }
    }

    private sealed class GraphSharePointIds
    {
        public string? ListItemId { get; set; }
    }

    private sealed class GraphListItem
    {
        public string? Id { get; set; }
    }

    private sealed class UploadSession
    {
        [JsonPropertyName("uploadUrl")]
        public string? UploadUrl { get; set; }
    }

    private sealed class UploadProgress
    {
        [JsonPropertyName("nextExpectedRanges")]
        public List<string>? NextExpectedRanges { get; set; }
    }

    private sealed class ChildrenPage
    {
        [JsonPropertyName("value")]
        public List<GraphDriveItem>? Items { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class SpoolFile : IAsyncDisposable
    {
        private readonly string path;
        private int disposed;

        public SpoolFile(string path, long length)
        {
            this.path = path;
            Length = length;
        }

        public long Length { get; }

        public FileStream OpenRead()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SpoolFile));
            }

            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                HashBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                TryDelete(path);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class GraphResponseStream : Stream
    {
        private readonly Stream inner;
        private readonly HttpResponseMessage response;
        private int disposed;

        public GraphResponseStream(Stream inner, HttpResponseMessage response)
        {
            this.inner = inner;
            this.response = response;
        }

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken)
            => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
            => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin)
            => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
            => inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
            => inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                await inner.DisposeAsync();
                response.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }

    private sealed class GraphStorageOperationException : DocumentStorageException
    {
        public GraphStorageOperationException(
            string message,
            bool mayHaveBeenAccepted,
            Exception? innerException = null)
            : base(message, innerException ?? new InvalidOperationException(message))
        {
            MayHaveBeenAccepted = mayHaveBeenAccepted;
        }

        public bool MayHaveBeenAccepted { get; }
    }

    private sealed class GraphMetadataMissingException : DocumentStorageException
    {
        public GraphMetadataMissingException()
            : base("The SharePoint metadata field is missing.")
        {
        }
    }

    private sealed class GraphUploadSessionExpiredException : DocumentStorageException
    {
        public GraphUploadSessionExpiredException()
            : base("The Graph upload session no longer exists.")
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A failed cleanup is not allowed to mask the storage result.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed cleanup is not allowed to mask the storage result.
        }
    }
}
