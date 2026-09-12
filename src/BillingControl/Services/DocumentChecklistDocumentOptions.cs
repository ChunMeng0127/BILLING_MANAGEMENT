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

    public static string ResolveDocumentName(string? selection, string? otherDocumentName)
    {
        var normalizedSelection = selection?.Trim();
        if (string.Equals(normalizedSelection, OtherDocuments, StringComparison.Ordinal))
        {
            var normalizedOtherName = otherDocumentName?.Trim();
            Finance.Require(!string.IsNullOrWhiteSpace(normalizedOtherName),
                "Enter the document name when Other Documents is selected.");
            Finance.Require(normalizedOtherName!.Length <= 160,
                "Document name must be 160 characters or fewer.");
            return normalizedOtherName;
        }

        Finance.Require(IsStandardDocument(normalizedSelection), "Select a document.");
        return normalizedSelection!;
    }

    public static DocumentChecklistDocumentMapping MapExistingDocument(string name)
    {
        var normalizedName = name.Trim();
        return IsStandardDocument(normalizedName)
            ? new(normalizedName, null)
            : new(OtherDocuments, normalizedName);
    }
}
