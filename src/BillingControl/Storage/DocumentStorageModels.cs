using System.Collections.Immutable;

namespace BillingControl.Storage;

/// <summary>
/// Immutable metadata and caller-owned content for one logical permanent object.
/// </summary>
public sealed record DocumentStorageRequest
{
    public DocumentStorageRequest(
        string logicalStorageKey,
        string originalFileName,
        string mimeType,
        long byteLength,
        string sha256Hash,
        Stream content,
        DateTimeOffset? receivedAt = null,
        string? source = null,
        string? sourceReference = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        LogicalStorageKey = DocumentStorageModelValidation.RequiredText(logicalStorageKey, nameof(logicalStorageKey));
        OriginalFileName = DocumentStorageModelValidation.RequiredText(originalFileName, nameof(originalFileName));
        MimeType = DocumentStorageModelValidation.RequiredText(mimeType, nameof(mimeType));
        if (byteLength < 0)
        {
            throw new DocumentStorageValidationException("ByteLength cannot be negative.");
        }

        ByteLength = byteLength;
        Sha256Hash = DocumentStorageModelValidation.Sha256Hash(sha256Hash);
        Content = content ?? throw new ArgumentNullException(nameof(content));
        ReceivedAt = receivedAt;
        Source = DocumentStorageModelValidation.OptionalText(source);
        SourceReference = DocumentStorageModelValidation.OptionalText(sourceReference);
        Metadata = DocumentStorageModelValidation.CopyMetadata(metadata);
    }

    public string LogicalStorageKey { get; }

    public string OriginalFileName { get; }

    public string MimeType { get; }

    public long ByteLength { get; }

    public string Sha256Hash { get; }

    /// <summary>
    /// The caller-owned stream. Storage implementations must not dispose it.
    /// </summary>
    public Stream Content { get; }

    public DateTimeOffset? ReceivedAt { get; }

    public string? Source { get; }

    public string? SourceReference { get; }

    public ImmutableDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Immutable provider-neutral identity and informational metadata for one stored object.
/// </summary>
/// <remarks>
/// ContainerId plus ObjectId is the canonical provider identity. WebUrl is informational
/// only and is never an anonymous/public sharing-link mechanism.
/// </remarks>
public sealed record DocumentStorageReference
{
    public DocumentStorageReference(
        string provider,
        string containerId,
        string objectId,
        string? eTag,
        string? webUrl,
        DateTimeOffset storedAt)
    {
        Provider = DocumentStorageModelValidation.RequiredText(provider, nameof(provider));
        ContainerId = DocumentStorageModelValidation.RequiredText(containerId, nameof(containerId));
        ObjectId = DocumentStorageModelValidation.RequiredText(objectId, nameof(objectId));
        ETag = DocumentStorageModelValidation.OptionalText(eTag);
        WebUrl = DocumentStorageModelValidation.OptionalText(webUrl);
        StoredAt = storedAt;
    }

    public string Provider { get; }

    public string ContainerId { get; }

    public string ObjectId { get; }

    public string? ETag { get; }

    /// <summary>
    /// Informational URL metadata only; it does not grant access or create a sharing link.
    /// </summary>
    public string? WebUrl { get; }

    public DateTimeOffset StoredAt { get; }
}

/// <summary>
/// Immutable provider-neutral metadata for a stored object.
/// </summary>
public sealed record DocumentStorageMetadata
{
    public DocumentStorageMetadata(
        DocumentStorageReference reference,
        string logicalStorageKey,
        string originalFileName,
        string mimeType,
        long byteLength,
        string sha256Hash,
        DateTimeOffset? receivedAt,
        string? source,
        string? sourceReference,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        LogicalStorageKey = DocumentStorageModelValidation.RequiredText(logicalStorageKey, nameof(logicalStorageKey));
        OriginalFileName = DocumentStorageModelValidation.RequiredText(originalFileName, nameof(originalFileName));
        MimeType = DocumentStorageModelValidation.RequiredText(mimeType, nameof(mimeType));
        if (byteLength < 0)
        {
            throw new DocumentStorageValidationException("ByteLength cannot be negative.");
        }

        ByteLength = byteLength;
        Sha256Hash = DocumentStorageModelValidation.Sha256Hash(sha256Hash);
        ReceivedAt = receivedAt;
        Source = DocumentStorageModelValidation.OptionalText(source);
        SourceReference = DocumentStorageModelValidation.OptionalText(sourceReference);
        Metadata = DocumentStorageModelValidation.CopyMetadata(metadata);
    }

    public DocumentStorageReference Reference { get; }

    public string LogicalStorageKey { get; }

    public string OriginalFileName { get; }

    public string MimeType { get; }

    public long ByteLength { get; }

    public string Sha256Hash { get; }

    public DateTimeOffset? ReceivedAt { get; }

    public string? Source { get; }

    public string? SourceReference { get; }

    public ImmutableDictionary<string, string> Metadata { get; }
}

public enum DocumentStorageVerificationStatus
{
    Exists,
    Missing,
    MetadataMismatch
}

/// <summary>
/// Result of a provider-neutral storage existence/reconciliation check.
/// </summary>
public sealed record DocumentStorageVerificationResult(
    DocumentStorageVerificationStatus Status,
    DocumentStorageReference Reference,
    string? Detail = null)
{
    public bool Exists => Status == DocumentStorageVerificationStatus.Exists;
}

internal static class DocumentStorageModelValidation
{
    public static string RequiredText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DocumentStorageValidationException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    public static string? OptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Sha256Hash(string? value)
    {
        var normalized = RequiredText(value, nameof(value)).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DocumentStorageValidationException("Sha256Hash must contain exactly 64 hexadecimal characters.");
        }

        return normalized;
    }

    public static ImmutableDictionary<string, string> CopyMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (metadata is null)
        {
            return builder.ToImmutable();
        }

        foreach (var entry in metadata)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new DocumentStorageValidationException("Metadata keys cannot be blank.");
            }

            if (entry.Value is null)
            {
                throw new DocumentStorageValidationException($"Metadata value for '{entry.Key}' cannot be null.");
            }

            var key = entry.Key.Trim();
            if (!builder.TryAdd(key, entry.Value))
            {
                throw new DocumentStorageValidationException($"Metadata key '{key}' is duplicated.");
            }
        }

        return builder.ToImmutable();
    }
}
