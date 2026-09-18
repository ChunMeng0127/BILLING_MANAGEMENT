namespace BillingControl.Storage;

/// <summary>
/// Provider-neutral permanent document storage boundary.
/// </summary>
/// <remarks>
/// Implementations must treat a stored object as immutable. The interface deliberately
/// has no ordinary delete, replace, overwrite, rename, move, or sharing-link operation.
/// Corrections are represented by later lifecycle/audit decisions outside this boundary.
/// </remarks>
public interface IDocumentStorage
{
    /// <summary>
    /// Stores a new immutable object, or reconciles an idempotent retry for the same
    /// logical storage key to the existing object.
    /// </summary>
    /// <param name="request">The immutable object metadata and caller-owned content stream.</param>
    /// <param name="cancellationToken">Cancels the storage operation.</param>
    /// <returns>The provider-neutral reference for the permanent object.</returns>
    /// <remarks>
    /// The implementation reads the stream from its current position through the end,
    /// but does not dispose it. The caller owns the input stream and remains responsible
    /// for rewinding or disposing it.
    /// </remarks>
    Task<DocumentStorageReference> StoreImmutableAsync(
        DocumentStorageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an existing stored object for reading through the application's authorization boundary.
    /// </summary>
    /// <returns>A readable stream, or <see langword="null"/> when the object is missing.</returns>
    /// <remarks>The caller owns and must dispose the returned stream.</remarks>
    Task<Stream?> OpenReadAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads provider-neutral metadata for an existing object.
    /// </summary>
    /// <returns>Metadata, or <see langword="null"/> when the object is missing.</returns>
    Task<DocumentStorageMetadata?> GetMetadataAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies whether an object still exists and whether its reference metadata agrees.
    /// </summary>
    Task<DocumentStorageVerificationResult> VerifyAsync(
        DocumentStorageReference reference,
        CancellationToken cancellationToken = default);
}
