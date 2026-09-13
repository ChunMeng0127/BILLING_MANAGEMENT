using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using BillingControl.Storage;
using BillingControl.Storage.SharePoint;
using Microsoft.Extensions.Options;

namespace BillingControl.Tests;

public sealed class GraphSharePointDocumentStorageTests
{
    [Fact]
    public async Task StoreImmutableAsync_uses_explicit_resources_and_persists_metadata_without_overwrite()
    {
        var bytes = Encoding.UTF8.GetBytes("small-content");
        var request = CreateRequest("small-content", bytes);
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Created,
            ItemJson("item-1", "etag-upload", request.ByteLength, request.MimeType)));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-1", "etag-upload", request.ByteLength, request.MimeType, "17")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-1", "etag-final", request.ByteLength, request.MimeType)));

        var provider = CreateProvider(handler);
        var reference = await provider.StoreImmutableAsync(request);

        Assert.Equal("MicrosoftGraphSharePoint", reference.Provider);
        Assert.Equal("drive-id", reference.ContainerId);
        Assert.Equal("item-1", reference.ObjectId);
        Assert.Equal("etag-final", reference.ETag);
        Assert.Equal("https://sharepoint.test/item-1", reference.WebUrl);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.Equal(bytes.Length, request.Content.Position);
        Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(handler.Requests[1].Body));
        Assert.Equal("*", handler.Requests[1].Headers["If-None-Match"]);
        Assert.Contains(
            "/v1.0/drives/drive-id/items/folder-id:/billing-control-artifact-",
            handler.Requests[1].Uri.AbsolutePath,
            StringComparison.Ordinal);
        Assert.Contains("/content", handler.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(
            "/v1.0/sites/site-id/lists/list-id/items/17/fields",
            handler.Requests[3].Uri.AbsolutePath);
        Assert.Contains(request.LogicalStorageKey, Encoding.UTF8.GetString(handler.Requests[3].Body));
        Assert.Contains(request.OriginalFileName, Encoding.UTF8.GetString(handler.Requests[3].Body));
        Assert.DoesNotContain("permissions", string.Join('\n', handler.Requests.Select(item => item.Uri.AbsoluteUri)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createLink", string.Join('\n', handler.Requests.Select(item => item.Uri.AbsoluteUri)), StringComparison.OrdinalIgnoreCase);
        Assert.All(
            handler.Requests,
            item => Assert.Equal("Bearer test-token", item.Headers["Authorization"]));
    }

    [Fact]
    public async Task StoreImmutableAsync_uses_resumable_session_with_sequential_ranges_and_no_bearer_on_upload_url()
    {
        var bytes = Enumerable.Range(0, 2 * ChunkSize)
            .Select(value => (byte)(value % 251))
            .ToArray();
        var request = CreateRequest("large-content", bytes);
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            "{\"uploadUrl\":\"https://upload.test/session-1\"}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Accepted,
            "{\"nextExpectedRanges\":[\"327680-\"]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Created,
            ItemJson("item-large", "etag-upload", request.ByteLength, request.MimeType)));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-large", "etag-upload", request.ByteLength, request.MimeType, "18")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-large", "etag-final", request.ByteLength, request.MimeType)));

        var provider = CreateProvider(handler, resumableThresholdBytes: 1);
        var reference = await provider.StoreImmutableAsync(request);

        Assert.Equal("item-large", reference.ObjectId);
        Assert.Equal(7, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        var sessionBody = Encoding.UTF8.GetString(handler.Requests[1].Body);
        Assert.Contains("\"@microsoft.graph.conflictBehavior\":\"fail\"", sessionBody);
        Assert.Equal("https://upload.test/session-1", handler.Requests[2].Uri.AbsoluteUri);
        Assert.Equal("bytes 0-327679/655360", handler.Requests[2].Headers["Content-Range"]);
        Assert.Equal("bytes 327680-655359/655360", handler.Requests[3].Headers["Content-Range"]);
        Assert.Equal(bytes[..ChunkSize], handler.Requests[2].Body);
        Assert.Equal(bytes[ChunkSize..], handler.Requests[3].Body);
        Assert.False(handler.Requests[2].Headers.ContainsKey("Authorization"));
        Assert.False(handler.Requests[3].Headers.ContainsKey("Authorization"));
        Assert.Equal(ChunkSize, int.Parse(handler.Requests[2].Headers["Content-Length"]!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(ChunkSize, int.Parse(handler.Requests[3].Headers["Content-Length"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task StoreImmutableAsync_recovers_accepted_chunk_after_416_by_querying_session_status()
    {
        var bytes = Enumerable.Range(0, 2 * ChunkSize)
            .Select(value => (byte)(value % 251))
            .ToArray();
        var request = CreateRequest("ambiguous-large-content", bytes);
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            "{\"uploadUrl\":\"https://upload.test/session-416\"}"));
        handler.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("simulated ambiguous transport failure")));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            "{\"nextExpectedRanges\":[\"327680-\"]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Created,
            ItemJson("item-416", "etag-upload", request.ByteLength, request.MimeType)));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-416", "etag-upload", request.ByteLength, request.MimeType, "24")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-416", "etag-final", request.ByteLength, request.MimeType)));

        var provider = CreateProvider(
            handler,
            resumableThresholdBytes: 1,
            maxAttempts: 2);
        var reference = await provider.StoreImmutableAsync(request);

        Assert.Equal("item-416", reference.ObjectId);
        Assert.Equal(9, handler.Requests.Count);
        Assert.Equal("bytes 0-327679/655360", handler.Requests[2].Headers["Content-Range"]);
        Assert.Equal("bytes 0-327679/655360", handler.Requests[3].Headers["Content-Range"]);
        Assert.Equal(handler.Requests[2].Body, handler.Requests[3].Body);
        Assert.Equal(HttpMethod.Get, handler.Requests[4].Method);
        Assert.Equal("https://upload.test/session-416", handler.Requests[4].Uri.AbsoluteUri);
        Assert.False(handler.Requests[4].Headers.ContainsKey("Authorization"));
        Assert.Equal("bytes 327680-655359/655360", handler.Requests[5].Headers["Content-Range"]);
        Assert.False(handler.Requests[5].Headers.ContainsKey("Authorization"));
        Assert.Equal(
            3,
            handler.Requests.Count(item =>
                item.Method == HttpMethod.Put &&
                string.Equals(item.Uri.Host, "upload.test", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            1,
            handler.Requests.Count(item =>
                item.Method == HttpMethod.Post &&
                item.Uri.AbsolutePath.Contains("createUploadSession", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("not-a-range")]
    [InlineData("655361-")]
    public async Task StoreImmutableAsync_rejects_malformed_or_invalid_416_session_status(
        string nextExpectedRange)
    {
        var bytes = Enumerable.Range(0, 2 * ChunkSize)
            .Select(value => (byte)(value % 251))
            .ToArray();
        var request = CreateRequest("invalid-416-status", bytes);
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            "{\"uploadUrl\":\"https://upload.test/session-invalid-416\"}"));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            JsonSerializer.Serialize(new { nextExpectedRanges = new[] { nextExpectedRange } })));

        var provider = CreateProvider(
            handler,
            resumableThresholdBytes: 1,
            maxAttempts: 2);

        await Assert.ThrowsAsync<DocumentStorageValidationException>(() =>
            provider.StoreImmutableAsync(request));

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[3].Method);
        Assert.False(handler.Requests[3].Headers.ContainsKey("Authorization"));
        Assert.DoesNotContain(
            handler.Requests,
            item => item.Uri.Host.Equals("upload.test", StringComparison.OrdinalIgnoreCase) &&
                    item.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GetMetadataAsync_reads_immutable_metadata_from_the_selected_list_field()
    {
        var request = CreateRequest("metadata-content");
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-2", "etag-2", request.ByteLength, request.MimeType, "19")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, FieldsJson(request)));

        var provider = CreateProvider(handler);
        var metadata = await provider.GetMetadataAsync(
            new DocumentStorageReference(
                "MicrosoftGraphSharePoint",
                "drive-id",
                "item-2",
                "etag-2",
                "https://sharepoint.test/item-2",
                DateTimeOffset.Parse("2026-09-13T00:00:00Z", CultureInfo.InvariantCulture)));

        Assert.NotNull(metadata);
        Assert.Equal(request.LogicalStorageKey, metadata.LogicalStorageKey);
        Assert.Equal(request.OriginalFileName, metadata.OriginalFileName);
        Assert.Equal(request.MimeType, metadata.MimeType);
        Assert.Equal(request.ByteLength, metadata.ByteLength);
        Assert.Equal(request.Sha256Hash, metadata.Sha256Hash);
        Assert.Equal(request.ReceivedAt, metadata.ReceivedAt);
        Assert.Equal(request.Source, metadata.Source);
        Assert.Equal(request.SourceReference, metadata.SourceReference);
        Assert.Equal(request.Metadata["origin"], metadata.Metadata["origin"]);
        Assert.Equal(
            "/v1.0/drives/drive-id/items/item-2",
            handler.Requests[0].Uri.AbsolutePath);
        Assert.Contains("expand=fields", handler.Requests[1].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMetadataAsync_accepts_equivalent_guid_metadata_list_id_formatting()
    {
        var listId = "8a7e4c10-4c9b-4b55-a6b6-5a6b1b8c9d10";
        var request = CreateRequest("guid-list-content");
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem(
                "item-guid-list",
                "etag-guid-list",
                request.ByteLength,
                request.MimeType,
                "25",
                listId: $"{{{listId.ToUpperInvariant()}}}")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, FieldsJson(request)));

        var provider = CreateProvider(
            handler,
            metadataListId: listId.ToLowerInvariant());
        var metadata = await provider.GetMetadataAsync(
            Reference("item-guid-list", "etag-guid-list"));

        Assert.NotNull(metadata);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetMetadataAsync_rejects_item_from_different_metadata_list_before_field_read()
    {
        var request = CreateRequest("mismatched-list-content");
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem(
                "item-wrong-list",
                "etag-wrong-list",
                request.ByteLength,
                request.MimeType,
                "26",
                listId: "different-list-id")));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<DocumentStorageValidationException>(() =>
            provider.GetMetadataAsync(Reference("item-wrong-list", "etag-wrong-list")));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StoreImmutableAsync_rejects_item_from_different_metadata_list_before_patch()
    {
        var request = CreateRequest("mismatched-upload-list");
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Created,
            ItemJson("item-wrong-upload-list", "etag-upload", request.ByteLength, request.MimeType)));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem(
                "item-wrong-upload-list",
                "etag-upload",
                request.ByteLength,
                request.MimeType,
                "27",
                listId: "different-list-id")));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<DocumentStorageValidationException>(() =>
            provider.StoreImmutableAsync(request));

        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, item => item.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task OpenReadAsync_returns_content_and_preserves_caller_ownership_of_returned_stream()
    {
        var bytes = Encoding.UTF8.GetBytes("readable-content");
        var handler = new RecordingHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        var provider = CreateProvider(handler);

        await using var content = await provider.OpenReadAsync(Reference("item-read", "etag-read"));
        Assert.NotNull(content);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal("Bearer test-token", handler.Requests.Single().Headers["Authorization"]);
    }

    [Fact]
    public async Task OpenReadAsync_follows_pre_authenticated_redirect_without_forwarding_bearer_token()
    {
        var bytes = Encoding.UTF8.GetBytes("redirected-content");
        var handler = new RecordingHandler();
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("https://download.test/pre-authenticated");
        handler.Enqueue(redirect);
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        var provider = CreateProvider(handler);

        await using var content = await provider.OpenReadAsync(Reference("item-read", "etag-read"));
        Assert.NotNull(content);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal("Bearer test-token", handler.Requests[0].Headers["Authorization"]);
        Assert.False(handler.Requests[1].Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task VerifyAsync_returns_exists_with_current_reference()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-exists", "etag-exists", 2, "application/pdf")));
        var provider = CreateProvider(handler);

        var result = await provider.VerifyAsync(Reference("item-exists", "etag-exists"));

        Assert.Equal(DocumentStorageVerificationStatus.Exists, result.Status);
        Assert.True(result.Exists);
        Assert.Equal("etag-exists", result.Reference.ETag);
    }

    [Fact]
    public async Task VerifyAsync_distinguishes_graph_404_as_missing()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);

        var result = await provider.VerifyAsync(Reference("item-missing", "etag-missing"));

        Assert.Equal(DocumentStorageVerificationStatus.Missing, result.Status);
        Assert.False(result.Exists);
    }

    [Fact]
    public async Task GetMetadataAsync_maps_graph_401_and_403_to_provider_neutral_auth_failure_without_retry()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            var handler = new RecordingHandler();
            handler.Enqueue(new HttpResponseMessage(status));
            var provider = CreateProvider(handler);

            await Assert.ThrowsAsync<DocumentStorageException>(() =>
                provider.GetMetadataAsync(Reference("item-auth", "etag-auth")));
            Assert.Single(handler.Requests);
        }
    }

    [Fact]
    public async Task VerifyAsync_honors_retry_after_for_429()
    {
        var handler = new RecordingHandler();
        var throttled = new HttpResponseMessage((HttpStatusCode)429);
        throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        handler.Enqueue(throttled);
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-throttled", "etag-throttled", 2, "application/pdf")));
        var delays = new List<TimeSpan>();
        var provider = CreateProvider(
            handler,
            maxAttempts: 2,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await provider.VerifyAsync(Reference("item-throttled", "etag-throttled"));

        Assert.True(result.Exists);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task VerifyAsync_retries_transient_5xx_with_bounded_backoff()
    {
        var handler = new RecordingHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-retry", "etag-retry", 2, "application/pdf")));
        var delays = new List<TimeSpan>();
        var provider = CreateProvider(
            handler,
            maxAttempts: 2,
            delayAsync: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await provider.VerifyAsync(Reference("item-retry", "etag-retry"));

        Assert.True(result.Exists);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.Zero, delays[0]);
    }

    [Fact]
    public async Task StoreImmutableAsync_rejects_malformed_graph_upload_response()
    {
        var request = CreateRequest("malformed-response");
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(HttpStatusCode.Created, "{}"));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<DocumentStorageValidationException>(() =>
            provider.StoreImmutableAsync(request));
    }

    [Fact]
    public async Task VerifyAsync_propagates_cancellation_to_graph_request()
    {
        var handler = new CancellationHandler();
        var provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();
        var operation = provider.VerifyAsync(Reference("item-cancel", "etag-cancel"), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task StoreImmutableAsync_does_not_dispose_caller_owned_input_stream()
    {
        var bytes = Encoding.UTF8.GetBytes("caller-owned");
        var request = CreateRequest("caller-owned-key", bytes);
        var trackingStream = new TrackingMemoryStream(bytes);
        request = new DocumentStorageRequest(
            request.LogicalStorageKey,
            request.OriginalFileName,
            request.MimeType,
            request.ByteLength,
            request.Sha256Hash,
            trackingStream,
            request.ReceivedAt,
            request.Source,
            request.SourceReference,
            request.Metadata);
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Created,
            ItemJson("item-stream", "etag-upload", request.ByteLength, request.MimeType)));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-stream", "etag-upload", request.ByteLength, request.MimeType, "21")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-stream", "etag-final", request.ByteLength, request.MimeType)));

        var provider = CreateProvider(handler);
        await provider.StoreImmutableAsync(request);

        Assert.False(trackingStream.WasDisposed);
        Assert.Equal(bytes.Length, trackingStream.Position);
    }

    [Fact]
    public async Task StoreImmutableAsync_reconciles_same_logical_key_without_duplicate_upload()
    {
        var request = CreateRequest("idempotent-content");
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{\"value\":[]}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Created,
            ItemJson("item-idempotent", "etag-upload", request.ByteLength, request.MimeType)));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-idempotent", "etag-upload", request.ByteLength, request.MimeType, "22")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, "{}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJson("item-idempotent", "etag-final", request.ByteLength, request.MimeType)));
        var provider = CreateProvider(handler);
        var first = await provider.StoreImmutableAsync(request);

        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            $"{{\"value\":[{ItemJson("item-idempotent", "etag-final", request.ByteLength, request.MimeType, StableFileName(request.LogicalStorageKey))}]}}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-idempotent", "etag-final", request.ByteLength, request.MimeType, "22")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, FieldsJson(request)));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("idempotent-content"))
        });

        var second = await provider.StoreImmutableAsync(CreateRequest("idempotent-content"));

        Assert.Equal(first, second);
        Assert.Equal(9, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests.Skip(5), item => item.Method == HttpMethod.Put);
        Assert.DoesNotContain(handler.Requests.Skip(5), item => item.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task StoreImmutableAsync_rejects_conflicting_immutable_metadata_without_reading_or_overwriting()
    {
        var request = CreateRequest("conflicting-content");
        var conflicting = CreateRequest("conflicting-content", Encoding.UTF8.GetBytes("different"));
        var handler = new RecordingHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            $"{{\"value\":[{ItemJson("item-conflict", "etag-conflict", request.ByteLength, request.MimeType, StableFileName(request.LogicalStorageKey))}]}}"));
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            ItemJsonWithListItem("item-conflict", "etag-conflict", request.ByteLength, request.MimeType, "23")));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, FieldsJson(conflicting)));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<DocumentStorageConflictException>(() =>
            provider.StoreImmutableAsync(request));

        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, item => item.Uri.AbsolutePath.EndsWith("/content", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, item => item.Method == HttpMethod.Put);
        Assert.DoesNotContain(handler.Requests, item => item.Method == HttpMethod.Patch);
    }

    [Fact]
    public void Provider_exposes_no_public_sharing_link_operation()
    {
        Assert.DoesNotContain(
            typeof(GraphSharePointDocumentStorage).GetMethods(),
            method => method.Name.Contains("Sharing", StringComparison.OrdinalIgnoreCase) ||
                      method.Name.Contains("CreateLink", StringComparison.OrdinalIgnoreCase));
    }

    private const int ChunkSize = 320 * 1024;

    private static GraphSharePointDocumentStorage CreateProvider(
        HttpMessageHandler handler,
        long resumableThresholdBytes = 10 * 1024 * 1024,
        int maxAttempts = 3,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        string metadataListId = "list-id")
    {
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var options = new SharePointDocumentStorageOptions
        {
            GraphBaseUrl = "https://graph.test/v1.0",
            SiteId = "site-id",
            DriveId = "drive-id",
            RootFolderItemId = "folder-id",
            MetadataListId = metadataListId,
            MetadataFieldInternalName = "BillingControlMetadata",
            ResumableUploadThresholdBytes = resumableThresholdBytes,
            UploadChunkSizeBytes = ChunkSize,
            MaxAttempts = maxAttempts,
            RetryBaseDelayMilliseconds = 0,
            RetryJitterMilliseconds = 0,
            MaxRetryDelayMilliseconds = 0
        };
        return new GraphSharePointDocumentStorage(
            client,
            new StaticTokenProvider(),
            Options.Create(options),
            delayAsync);
    }

    private static DocumentStorageRequest CreateRequest(
        string logicalStorageKey,
        byte[]? bytes = null)
    {
        bytes ??= Encoding.UTF8.GetBytes(logicalStorageKey);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new DocumentStorageRequest(
            logicalStorageKey,
            "original-document.pdf",
            "application/pdf",
            bytes.LongLength,
            hash,
            new MemoryStream(bytes, writable: false),
            DateTimeOffset.Parse("2026-09-13T01:02:03Z", CultureInfo.InvariantCulture),
            "WhatsApp",
            "media-reference",
            new Dictionary<string, string>
            {
                ["origin"] = "test"
            });
    }

    private static DocumentStorageReference Reference(string objectId, string eTag)
        => new(
            "MicrosoftGraphSharePoint",
            "drive-id",
            objectId,
            eTag,
            $"https://sharepoint.test/{objectId}",
            DateTimeOffset.Parse("2026-09-13T00:00:00Z", CultureInfo.InvariantCulture));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string ItemJson(
        string id,
        string eTag,
        long size,
        string mimeType,
        string name = "billing-control-artifact-test.bin")
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["size"] = size,
            ["eTag"] = eTag,
            ["webUrl"] = $"https://sharepoint.test/{id}",
            ["createdDateTime"] = "2026-09-13T01:02:03Z",
            ["file"] = new Dictionary<string, string?>
            {
                ["mimeType"] = mimeType
            }
        });

    private static string ItemJsonWithListItem(
        string id,
        string eTag,
        long size,
        string mimeType,
        string listItemId,
        string listId = "list-id",
        string name = "billing-control-artifact-test.bin")
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["size"] = size,
            ["eTag"] = eTag,
            ["webUrl"] = $"https://sharepoint.test/{id}",
            ["createdDateTime"] = "2026-09-13T01:02:03Z",
            ["file"] = new Dictionary<string, string?>
            {
                ["mimeType"] = mimeType
            },
            ["sharepointIds"] = new Dictionary<string, string?>
            {
                ["listItemId"] = listItemId,
                ["listId"] = listId
            },
            ["listItem"] = new Dictionary<string, string?>
            {
                ["id"] = listItemId
            }
        });

    private static string StableFileName(string logicalStorageKey)
        => $"billing-control-artifact-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logicalStorageKey))).ToLowerInvariant()}.bin";

    private static string FieldsJson(DocumentStorageRequest request)
    {
        var envelope = new
        {
            logicalStorageKey = request.LogicalStorageKey,
            originalFileName = request.OriginalFileName,
            mimeType = request.MimeType,
            byteLength = request.ByteLength,
            sha256Hash = request.Sha256Hash,
            receivedAt = request.ReceivedAt,
            source = request.Source,
            sourceReference = request.SourceReference,
            metadata = request.Metadata
        };
        var envelopeJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "17",
            ["fields"] = new Dictionary<string, string?>
            {
                ["BillingControlMetadata"] = envelopeJson
            }
        });
    }

    private sealed class StaticTokenProvider : IGraphAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("test-token");
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> steps = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(HttpResponseMessage response)
            => steps.Enqueue((_, _) => Task.FromResult(response));

        public void Enqueue(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> step)
            => steps.Enqueue(step);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var headers = request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(item => item.Key, item => string.Join(",", item.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, headers, body));
            if (steps.Count == 0)
            {
                throw new InvalidOperationException("The test handler received an unexpected request.");
            }

            return await steps.Dequeue()(request, cancellationToken);
        }
    }

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        Dictionary<string, string> Headers,
        byte[] Body);

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
