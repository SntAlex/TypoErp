using Shouldly;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.SharedKernel.Tests.Normalization;

public sealed class StringNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("\t\r\n", "")]
    [InlineData("  МоЙ   ТЕг \t ", "мой тег")]
    [InlineData("45К   АРТИКУЛ\t\tТЕСТ", "45к артикул тест")]
    [InlineData("простой", "простой")]
    [InlineData("  Multiple   Spaces   And \n Newlines  ", "multiple spaces and newlines")]
    public void Normalize_ShouldTrimCollapseWhitespaceAndLowercase(string? input, string expected)
    {
        var result = StringNormalizer.Normalize(input);

        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  Cover_1   (2).JPG ", "cover_1 (2).jpg")]
    [InlineData("CARD_1.PDF", "card_1.pdf")]
    [InlineData("  Video   1 .MP4 ", "video 1.mp4")]
    [InlineData("  .typoerp_id  ", ".typoerp_id")]
    [InlineData("  .DS_Store ", ".ds_store")]
    [InlineData("  README   FILE  ", "readme file")]
    [InlineData("  My.Archive   V1.TAR.GZ ", "my.archive v1.tar.gz")]
    [InlineData(".", ".")]
    [InlineData("..", "..")]
    [InlineData("card_1.pdf.", "card_1.pdf.")]
    [InlineData("bad/card_1.PDF", "bad/card_1.pdf")]
    [InlineData("bad\\card_1.PDF", "bad\\card_1.pdf")]
    public void NormalizeFileName_ShouldNormalizeNameAndExtensionSeparatelyAcrossPlatforms(
        string? input,
        string expected
    )
    {
        var result = StringNormalizer.NormalizeFileName(input);

        result.ShouldBe(expected);
    }
}
