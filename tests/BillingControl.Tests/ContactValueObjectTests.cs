using BillingControl.Services;

namespace BillingControl.Tests;

public sealed class ContactValueObjectTests
{
    [Theory]
    [InlineData("+60123456789", "+60123456789")]
    [InlineData("  +60 12-345 (6789)  ", "+60123456789")]
    [InlineData("+(60) 12-345 6789", "+60123456789")]
    public void NormalizeE164AcceptsCanonicalAndHarmlessPresentationFormatting(
        string input,
        string expected)
    {
        Assert.Equal(expected, ContactValueObjects.NormalizeE164(input));
    }

    [Theory]
    [InlineData("60123456789")]
    [InlineData("0060123456789")]
    [InlineData("+60 12 345 6789 ext 1")]
    [InlineData("+60.123456789")]
    [InlineData("+60/123456789")]
    [InlineData("+６０１２３４５６７８９")]
    [InlineData("+6012345678901234")]
    [InlineData("+00123456789")]
    public void NormalizeE164RejectsNonCanonicalInput(string input)
    {
        Assert.Throws<BusinessException>(() => ContactValueObjects.NormalizeE164(input));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("en", "en")]
    [InlineData("EN-my", "en-MY")]
    [InlineData("ms-MY", "ms-MY")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("ZH-hANT-my", "zh-Hant-MY")]
    public void NormalizePreferredLanguageIsDeterministic(
        string? input,
        string? expected)
    {
        Assert.Equal(expected, ContactValueObjects.NormalizePreferredLanguage(input));
    }

    [Theory]
    [InlineData("en_MY")]
    [InlineData("en MY")]
    [InlineData("en- -MY")]
    [InlineData("-en")]
    [InlineData("en-")]
    [InlineData("English (Malaysia)")]
    [InlineData("en.MY")]
    public void NormalizePreferredLanguageRejectsInvalidTags(string input)
    {
        Assert.Throws<BusinessException>(() => ContactValueObjects.NormalizePreferredLanguage(input));
    }

    [Fact]
    public void NormalizePreferredLanguageEnforcesPersistenceLimit()
    {
        var input = "abcdefgh-" + new string('x', 30);
        Assert.Throws<BusinessException>(() => ContactValueObjects.NormalizePreferredLanguage(input));
    }
}
