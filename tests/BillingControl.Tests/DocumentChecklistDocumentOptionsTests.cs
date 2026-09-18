using BillingControl.Services;

namespace BillingControl.Tests;

public class DocumentChecklistDocumentOptionsTests
{
    [Fact]
    public void StandardDocumentsUseTheProspectivePhaseTwoChoices()
    {
        Assert.Equal(
            [
                "Bank Statement",
                "Sales Invoice",
                "Purchase Invoice",
                "Expenses Invoice",
                "Staff Claim",
                "Payroll Report",
                "Payment Voucher",
                "Official Receipt"
            ],
            DocumentChecklistDocumentOptions.StandardDocuments);
        Assert.False(DocumentChecklistDocumentOptions.IsStandardDocument("Payment / Receipt"));
    }

    [Fact]
    public void StandardDocumentIsReturnedAsThePersistedName()
    {
        Assert.Equal("Bank Statement", DocumentChecklistDocumentOptions.ResolveDocumentName(" Bank Statement "));
        Assert.True(DocumentChecklistDocumentOptions.IsStandardDocument("Payroll Report"));
    }

    [Fact]
    public void CustomDocumentNameUsesTheSameFieldAndIsTrimmedAndRequired()
    {
        Assert.Equal("Loan Statement", DocumentChecklistDocumentOptions.ResolveDocumentName("  Loan Statement  "));
        var mapping = DocumentChecklistDocumentOptions.MapExistingDocument("Loan Statement");
        Assert.Equal("Loan Statement", mapping.Selection);
        Assert.Null(mapping.OtherDocumentName);
        Assert.Throws<BusinessException>(() => DocumentChecklistDocumentOptions.ResolveDocumentName(DocumentChecklistDocumentOptions.OtherDocuments));
        Assert.Throws<BusinessException>(() => DocumentChecklistDocumentOptions.ResolveDocumentName(new string('x', 161)));
    }

    [Fact]
    public void ExistingStandardDocumentMapsBackToItsDropdownOption()
    {
        var mapping = DocumentChecklistDocumentOptions.MapExistingDocument("Bank Statement");

        Assert.Equal("Bank Statement", mapping.Selection);
        Assert.Null(mapping.OtherDocumentName);
    }

    [Fact]
    public void ExistingLegacyPaymentReceiptRemainsReadableAndEditable()
    {
        var mapping = DocumentChecklistDocumentOptions.MapExistingDocument("Payment / Receipt");

        Assert.Equal("Payment / Receipt", mapping.Selection);
        Assert.Equal("Payment / Receipt", DocumentChecklistDocumentOptions.ResolveDocumentName(mapping.Selection));
        Assert.Equal("Payment Voucher", DocumentChecklistDocumentOptions.ResolveDocumentName("Payment Voucher"));
        Assert.Equal("Official Receipt", DocumentChecklistDocumentOptions.ResolveDocumentName("Official Receipt"));
    }
}
