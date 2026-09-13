using System.Security.Cryptography;
using System.Text;
using BillingControl.Storage;

namespace BillingControl.Tests;

public sealed class DocumentStorageTests
{
    [Fact]
    public async Task StoreImmutableObjectReturnsStableReference()
    {
        var storage = new DeterministicInMemoryDocumentStorage();

        var first = await storage.StoreImmutableAsync(CreateRequest("received/one", "same content"));
        var retry = await storage.StoreImmutableAsync(CreateRequest("received/one", "same content"));

        Assert.Same(first, retry);
        Assert.Equal(first, retry);
    }

    [Fact]
    public async Task StoredReferenceContainsStableProviderContainerAndObjectIdentifiers()
    {
        var storage = new DeterministicInMemoryDocumentStorage();

        var reference = await storage.StoreImmutableAsync(CreateRequest("received/identifiers", "content"));

        Assert.Equal("DeterministicFake", reference.Provider);
        Assert.False(string.IsNullOrWhiteSpace(reference.ContainerId));
        Assert.False(string.IsNullOrWhiteSpace(reference.ObjectId));
        Assert.False(string.IsNullOrWhiteSpace(reference.ETag));
        Assert.Null(reference.WebUrl);
        Assert.Equal(DateTimeOffset.UnixEpoch, reference.StoredAt);
    }

    [Fact]
    public async Task SameLogicalKeyAndSameContentRetryReturnsSameReference()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        var receivedAt = new DateTimeOffset(2026, 9, 13, 10, 30, 0, TimeSpan.Zero);

        var first = await storage.StoreImmutableAsync(CreateRequest(
            "received/retry",
            "retry-safe",
            receivedAt,
            sourceReference: "provider-message-1"));
        var retry = await storage.StoreImmutableAsync(CreateRequest(
            "received/retry",
            "retry-safe",
            receivedAt,
            sourceReference: "provider-message-1"));

        Assert.Same(first, retry);
        Assert.Equal(1, storage.StoredObjectCount);
    }

    [Fact]
    public async Task IdempotentRetryDoesNotCreateSecondFakeObject()
    {
        var storage = new DeterministicInMemoryDocumentStorage();

        await storage.StoreImmutableAsync(CreateRequest("received/count", "one"));
        await storage.StoreImmutableAsync(CreateRequest("received/count", "one"));

        Assert.Equal(1, storage.StoredObjectCount);
    }

    [Fact]
    public async Task ConflictingContentForSameLogicalKeyIsRejectedWithoutOverwrite()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        var original = await storage.StoreImmutableAsync(CreateRequest("received/conflict", "original"));

        var exception = await Assert.ThrowsAsync<DocumentStorageConflictException>(() =>
            storage.StoreImmutableAsync(CreateRequest("received/conflict", "modified")));

        Assert.Equal("received/conflict", exception.LogicalStorageKey);
        Assert.Equal(1, storage.StoredObjectCount);
        var readBack = await ReadAllAsync(await storage.OpenReadAsync(original));
        Assert.Equal("original", readBack);
    }

    [Fact]
    public async Task ReadReturnsOriginalBytesAndDoesNotDisposeCallerOwnedInputStream()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("original bytes"));
        var bytes = input.ToArray();
        var request = CreateRequest("received/read", bytes, input);

        var reference = await storage.StoreImmutableAsync(request);
        Assert.True(input.CanRead);

        var readBack = await ReadBytesAsync(await storage.OpenReadAsync(reference));
        Assert.Equal(bytes, readBack);
    }

    [Fact]
    public async Task MetadataAndReferenceLookupReturnsImmutableValues()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        var sourceMetadata = new Dictionary<string, string>
        {
            ["ReceivedDocumentId"] = "rd-123",
            ["Classification"] = "unclassified"
        };
        var request = CreateRequest(
            "received/metadata",
            "metadata bytes",
            new DateTimeOffset(2026, 9, 13, 11, 0, 0, TimeSpan.Zero),
            source: "WhatsApp",
            sourceReference: "media-123",
            metadata: sourceMetadata);
        sourceMetadata["ReceivedDocumentId"] = "changed-after-request";

        var reference = await storage.StoreImmutableAsync(request);
        var metadata = await storage.GetMetadataAsync(reference);

        Assert.NotNull(metadata);
        Assert.Same(reference, metadata!.Reference);
        Assert.Equal(request.LogicalStorageKey, metadata.LogicalStorageKey);
        Assert.Equal(request.OriginalFileName, metadata.OriginalFileName);
        Assert.Equal(request.MimeType, metadata.MimeType);
        Assert.Equal(request.ByteLength, metadata.ByteLength);
        Assert.Equal(request.Sha256Hash, metadata.Sha256Hash);
        Assert.Equal(request.ReceivedAt, metadata.ReceivedAt);
        Assert.Equal(request.Source, metadata.Source);
        Assert.Equal(request.SourceReference, metadata.SourceReference);
        Assert.Equal("rd-123", metadata.Metadata["ReceivedDocumentId"]);
        Assert.Equal("unclassified", metadata.Metadata["Classification"]);
    }

    [Fact]
    public async Task VerifyExistingObjectReturnsExists()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        var reference = await storage.StoreImmutableAsync(CreateRequest("received/verify", "present"));

        var result = await storage.VerifyAsync(reference);

        Assert.Equal(DocumentStorageVerificationStatus.Exists, result.Status);
        Assert.True(result.Exists);
        Assert.Same(reference, result.Reference);
    }

    [Fact]
    public async Task SimulatedExternalDisappearanceReturnsMissingAndRetryReconcilesSameObject()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        var request = CreateRequest("received/missing", "reconcile me");
        var reference = await storage.StoreImmutableAsync(request);

        storage.SimulateExternalDisappearance(reference);

        var missing = await storage.VerifyAsync(reference);
        Assert.Equal(DocumentStorageVerificationStatus.Missing, missing.Status);
        Assert.False(missing.Exists);
        Assert.Null(await storage.OpenReadAsync(reference));
        Assert.Null(await storage.GetMetadataAsync(reference));

        var reconciled = await storage.StoreImmutableAsync(CreateRequest("received/missing", "reconcile me"));
        Assert.Same(reference, reconciled);
        Assert.Equal(1, storage.StoredObjectCount);
        Assert.Equal(DocumentStorageVerificationStatus.Exists, (await storage.VerifyAsync(reference)).Status);
    }

    [Fact]
    public void IDocumentStorageHasNoOrdinaryDeleteReplaceOrOverwriteOperation()
    {
        var methodNames = typeof(IDocumentStorage)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Replace", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Overwrite", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rename", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Move", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WebUrlIsMetadataOnlyAndNoAnonymousSharingApiExists()
    {
        var storage = new DeterministicInMemoryDocumentStorage();
        var reference = await storage.StoreImmutableAsync(CreateRequest("received/url", "metadata only"));
        var methodNames = typeof(IDocumentStorage)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        Assert.Null(reference.WebUrl);
        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Share", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Public", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Anonymous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StorageAbstractionRequiresNoGraphOrCredentialAssembly()
    {
        var assemblyNames = typeof(IDocumentStorage).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(assemblyNames, name =>
            name.Equals("Microsoft.Graph", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.Graph.", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Azure.Identity", StringComparison.OrdinalIgnoreCase));
    }

    private static DocumentStorageRequest CreateRequest(
        string logicalStorageKey,
        string text,
        DateTimeOffset? receivedAt = null,
        string source = "Test",
        string? sourceReference = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return CreateRequest(logicalStorageKey, bytes, new MemoryStream(bytes), receivedAt, source, sourceReference, metadata);
    }

    private static DocumentStorageRequest CreateRequest(
        string logicalStorageKey,
        byte[] bytes,
        Stream content,
        DateTimeOffset? receivedAt = null,
        string source = "Test",
        string? sourceReference = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            logicalStorageKey,
            "received.pdf",
            "application/pdf",
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)),
            content,
            receivedAt,
            source,
            sourceReference,
            metadata);

    private static async Task<string> ReadAllAsync(Stream? stream)
    {
        Assert.NotNull(stream);
        using var ownedStream = stream!;
        using var reader = new StreamReader(ownedStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task<byte[]> ReadBytesAsync(Stream? stream)
    {
        Assert.NotNull(stream);
        using var ownedStream = stream!;
        using var buffer = new MemoryStream();
        await ownedStream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
