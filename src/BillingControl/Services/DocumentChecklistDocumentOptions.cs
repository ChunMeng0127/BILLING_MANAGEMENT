namespace BillingControl.Services;

public sealed record DocumentChecklistDocumentMapping(string Selection, string? OtherDocumentName);

public static class DocumentChecklistDocumentOptions
{
    public const string OtherDocuments = "Other Documents";

    public static IReadOnlyList<string> StandardDocuments { get; } =
    [
        "Bank Statement",
        "Sales Invoice",
        "Purchase Invoice",
        "Expenses Invoice",
        "Staff Claim",
        "Payroll Report",
        "Payment / Receipt"
    ];

    public static bool IsStandardDocument(string? value) =>
        StandardDocuments.Contains(value?.Trim() ?? "", StringComparer.Ordinal);

    public static string ResolveDocumentName(string? documentValue)
    {
        var normalizedValue = documentValue?.Trim();
        Finance.Require(!string.IsNullOrWhiteSpace(normalizedValue), "Enter or select a document.");
        if (string.Equals(normalizedValue, OtherDocuments, StringComparison.Ordinal))
        {
            Finance.Require(false,
                "Enter the document name when Other Documents is selected.");
        }

        Finance.Require(normalizedValue!.Length <= 160,
            "Document name must be 160 characters or fewer.");
        return normalizedValue;
    }

    // Kept for compatibility with older internal callers while the staff UI uses one field.
    public static string ResolveDocumentName(string? selection, string? otherDocumentName)
    {
        if (string.Equals(selection?.Trim(), OtherDocuments, StringComparison.Ordinal))
        {
            var normalizedOtherName = otherDocumentName?.Trim();
            Finance.Require(!string.IsNullOrWhiteSpace(normalizedOtherName),
                "Enter the document name when Other Documents is selected.");
            return ResolveDocumentName(normalizedOtherName);
        }

        return ResolveDocumentName(selection);
    }

    public static DocumentChecklistDocumentMapping MapExistingDocument(string name)
    {
        var normalizedName = name.Trim();
        return new(normalizedName, null);
    }
}
