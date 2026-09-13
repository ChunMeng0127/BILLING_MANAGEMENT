using System.Security.Cryptography;
using System.Text;

namespace BillingControl.Storage;

/// <summary>
/// Deterministic in-memory implementation for unit tests and retry/reconciliation tests.
/// </summary>
/// <remarks>
/// This fake is not production storage. It keeps an immutable logical-content snapshot so
/// conflicting retries remain detectable even after its simulated physical object disappears.
/// Physical object identifiers are derived from the logical key, and the default StoredAt value
/// is fixed at Unix epoch to keep tests repeatable.
/// </remarks>
public sealed class DeterministicInMemoryDocumentStorage : IDocumentStorage
{
    private const string DefaultProvider = "DeterministicFake";
    private const string DefaultContainerId = "fake-container";
    private static readonly DateTimeOffset DefaultStoredAt = DateTimeOffset.UnixEpoch;
    private readonly object gate = new();
    private readonly Dictionary<string, StoredObject> objectsByLogicalKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoredObject> objectsByObjectId = new(StringComparer.Ordinal);
    private readonly string provider;
    private readonly string containerId;
    private readonly DateTimeOffset storedAt;

    public DeterministicInMemoryDocumentStorage(
        string provider = DefaultProvider,
        string containerId = DefaultContainerId,
        DateTimeOffset? storedAt = null)
    {
        this.provider = DocumentStorageModelValidation.RequiredText(provider, nameof(provider));
        this.containerId = DocumentStorageModelValidation.RequiredText(containerId, nameof(containerId));
        this.storedAt = storedAt ?? DefaultStoredAt;
    }

    /// <summary>
    /// Number of currently present physical fake objects. This observation is test-only.
    /// </summary>
    public int StoredObjectCount
    {
        get
        {
            lock (gate)
            {
                return objectsByObjectId.Values.Count(item => item.PhysicalContent is not null);
            }
        }
    }

    public async Task<DocumentStorageReference> StoreImmutableAsync(
        DocumentStorageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Content.CanRead)
        {
            throw new DocumentStorageValidationException("The content stream must be readable.");
        }

        var content = await ReadAndValidateContentAsync(request, cancellationToken);

        lock (gate)
        {
            if (objectsByLogicalKey.TryGetValue(request.LogicalStorageKey, out var existing))
            {
                EnsureSameImmutableRequest(existing, request);
                if (!content.AsSpan().SequenceEqual(existing.ImmutableContent))
                {
                    throw new DocumentStorageConflictException(
                        request.LogicalStorageKey,
                        "The logical storage key was reused with different immutable content.");
                }

                if (existing.PhysicalContent is null)
                {
                    // Reconciliation restores the same logical object identity; it does not create
                    // a second object or change the original reference.
                    existing.PhysicalContent = content;
                }

                return existing.Reference;
            }

            var reference = new DocumentStorageReference(
                provider,
                containerId,
                CreateObjectId(request.LogicalStorageKey),
                $"fake-{request.Sha256Hash}",
                webUrl: null,
                storedAt);
            var metadata = new DocumentStorageMetadata(
                reference,
                request.LogicalStorageKey,
                request.OriginalFileName,
                request.MimeType,
                request.ByteLength,
                request.Sha256Hash,
                request.ReceivedAt,
                request.Source,
                request.SourceReference,
                request.Metadata);
            var stored = new StoredObject(reference, metadata, content);
            if (!objectsByObjectId.TryAdd(reference.ObjectId, stored))
            {
                throw new DocumentStorageException("The deterministic object identifier collided with an existing object.");
            }

            objectsByLogicalKey.Add(request.LogicalStorageKey, stored);
            return reference;
        }
    }

    public Task<Stream?> OpenReadAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var stored = FindStoredObject(reference);
            if (stored?.PhysicalContent is null)
            {
                return Task.FromResult<Stream?>(null);
            }

            var copy = stored.PhysicalContent.ToArray();
            return Task.FromResult<Stream?>(new MemoryStream(copy, writable: false));
        }
    }

    public Task<DocumentStorageMetadata?> GetMetadataAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var stored = FindStoredObject(reference);
            return Task.FromResult<DocumentStorageMetadata?>(
                stored?.PhysicalContent is null ? null : stored.Metadata);
        }
    }

    public Task<DocumentStorageVerificationResult> VerifyAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var stored = FindStoredObject(reference);
            if (stored is null || stored.PhysicalContent is null)
            {
                return Task.FromResult(new DocumentStorageVerificationResult(
                    DocumentStorageVerificationStatus.Missing,
                    reference,
                    "The object is not present in the provider."));
            }

            if (!string.Equals(reference.ETag, stored.Reference.ETag, StringComparison.Ordinal))
            {
                return Task.FromResult(new DocumentStorageVerificationResult(
                    DocumentStorageVerificationStatus.MetadataMismatch,
                    reference,
                    "The supplied version tag does not match the stored object."));
            }

            return Task.FromResult(new DocumentStorageVerificationResult(
                DocumentStorageVerificationStatus.Exists,
                stored.Reference));
        }
    }

    /// <summary>
    /// Test-only control that models an external deletion without adding deletion to
    /// <see cref="IDocumentStorage"/>. The logical record and immutable reference remain
    /// available for a later reconciliation retry.
    /// </summary>
    public void SimulateExternalDisappearance(DocumentStorageReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        lock (gate)
        {
            var stored = FindStoredObject(reference)
                ?? throw new DocumentStorageException("The fake object was not found.");
            stored.PhysicalContent = null;
        }
    }

    private async Task<byte[]> ReadAndValidateContentAsync(
        DocumentStorageRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ByteLength > int.MaxValue)
        {
            throw new DocumentStorageValidationException(
                "The in-memory fake cannot store an object larger than Int32.MaxValue bytes.");
        }

        await using var buffer = new MemoryStream(checked((int)request.ByteLength));
        await request.Content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != request.ByteLength)
        {
            throw new DocumentStorageValidationException(
                $"The content length ({buffer.Length}) does not match ByteLength ({request.ByteLength}).");
        }

        var content = buffer.ToArray();
        var actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(actualHash, request.Sha256Hash, StringComparison.Ordinal))
        {
            throw new DocumentStorageValidationException("The content SHA-256 does not match Sha256Hash.");
        }

        return content;
    }

    private static void EnsureSameImmutableRequest(
        StoredObject existing,
        DocumentStorageRequest request)
    {
        if (!string.Equals(existing.Metadata.OriginalFileName, request.OriginalFileName, StringComparison.Ordinal) ||
            !string.Equals(existing.Metadata.MimeType, request.MimeType, StringComparison.Ordinal) ||
            existing.Metadata.ByteLength != request.ByteLength ||
            !string.Equals(existing.Metadata.Sha256Hash, request.Sha256Hash, StringComparison.Ordinal) ||
            existing.Metadata.ReceivedAt != request.ReceivedAt ||
            !string.Equals(existing.Metadata.Source, request.Source, StringComparison.Ordinal) ||
            !string.Equals(existing.Metadata.SourceReference, request.SourceReference, StringComparison.Ordinal) ||
            !MetadataEquals(existing.Metadata.Metadata, request.Metadata))
        {
            throw new DocumentStorageConflictException(
                request.LogicalStorageKey,
                "The logical storage key was reused with conflicting immutable metadata.");
        }
    }

    private static bool MetadataEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var value) ||
                !string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private StoredObject? FindStoredObject(DocumentStorageReference reference)
    {
        if (!string.Equals(reference.Provider, provider, StringComparison.Ordinal) ||
            !string.Equals(reference.ContainerId, containerId, StringComparison.Ordinal))
        {
            return null;
        }

        return objectsByObjectId.TryGetValue(reference.ObjectId, out var stored)
            ? stored
            : null;
    }

    private static string CreateObjectId(string logicalStorageKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(logicalStorageKey);
        var keyHash = Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant();
        return $"object-{keyHash}";
    }

    private sealed class StoredObject(
        DocumentStorageReference reference,
        DocumentStorageMetadata metadata,
        byte[] immutableContent)
    {
        public DocumentStorageReference Reference { get; } = reference;

        public DocumentStorageMetadata Metadata { get; } = metadata;

        public byte[] ImmutableContent { get; } = immutableContent.ToArray();

        public byte[]? PhysicalContent { get; set; } = immutableContent;
    }
}
