using BillingControl.Services;

namespace BillingControl.Tests;

public sealed class DocumentChecklistKeyGeneratorTests
{
    [Theory]
    [InlineData("Yearly Bookkeeping - Standard Documents", "yearly-bookkeeping-standard-documents")]
    [InlineData("  Monthly__Bookkeeping  ", "monthly-bookkeeping")]
    [InlineData("A punctuation: friendly checklist!", "a-punctuation-friendly-checklist")]
    [InlineData("!!!", "checklist")]
    public void SlugifyCreatesStableBoundedStaffInvisibleKeys(string input, string expected)
    {
        Assert.Equal(expected, DocumentChecklistKeyGenerator.Slugify(input, "checklist"));
    }

    [Fact]
    public void AllocateUniqueUsesCaseInsensitiveNumericSuffixesWithinTheExistingLimit()
    {
        var baseKey = new string('a', 100);

        Assert.Equal($"{new string('a', 98)}-2",
            DocumentChecklistKeyGenerator.AllocateUnique(baseKey, [baseKey, $"{new string('a', 98)}-3"]));
    }
}
