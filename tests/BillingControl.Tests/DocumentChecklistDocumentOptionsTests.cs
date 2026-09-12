using BillingControl.Services;

namespace BillingControl.Tests;

public class DocumentChecklistDocumentOptionsTests
{
    [Fact]
    public void StandardDocumentIsReturnedAsThePersistedName()
    {
        Assert.Equal("Bank Statement", DocumentChecklistDocumentOptions.ResolveDocumentName(" Bank Statement ", "Ignored"));
        Assert.True(DocumentChecklistDocumentOptions.IsStandardDocument("Payroll Report"));
    }

    [Fact]
    public void OtherDocumentNameIsTrimmedAndRequired()
    {
        Assert.Equal("Loan Statement", DocumentChecklistDocumentOptions.ResolveDocumentName("Other Documents", "  Loan Statement  "));
        var mapping = DocumentChecklistDocumentOptions.MapExistingDocument("Loan Statement");
        Assert.Equal(DocumentChecklistDocumentOptions.OtherDocuments, mapping.Selection);
        Assert.Equal("Loan Statement", mapping.OtherDocumentName);
        Assert.Throws<BusinessException>(() => DocumentChecklistDocumentOptions.ResolveDocumentName("Other Documents", "  "));
        Assert.Throws<BusinessException>(() => DocumentChecklistDocumentOptions.ResolveDocumentName("Other Documents", new string('x', 161)));
    }

    [Fact]
    public void ExistingStandardDocumentMapsBackToItsDropdownOption()
    {
        var mapping = DocumentChecklistDocumentOptions.MapExistingDocument("Bank Statement");

        Assert.Equal("Bank Statement", mapping.Selection);
        Assert.Null(mapping.OtherDocumentName);
    }
}
