namespace BillingControl.Storage;

/// <summary>
/// Base exception for provider-neutral document-storage failures.
/// </summary>
public class DocumentStorageException : Exception
{
    public DocumentStorageException(string message)
        : base(message)
    {
    }

    public DocumentStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Indicates that a storage request is malformed or its content does not match its declared metadata.
/// </summary>
public sealed class DocumentStorageValidationException : DocumentStorageException
{
    public DocumentStorageValidationException(string message)
        : base(message)
    {
    }

    public DocumentStorageValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Indicates that a logical storage key was reused with different immutable content or metadata.
/// </summary>
public sealed class DocumentStorageConflictException : DocumentStorageException
{
    public DocumentStorageConflictException(string logicalStorageKey, string message)
        : base(message)
    {
        LogicalStorageKey = logicalStorageKey;
    }

    public string LogicalStorageKey { get; }
}
