using Shouldly;
using TypoErp.SharedKernel.Files;

namespace TypoErp.SharedKernel.Tests.Files;

public sealed class FilePrefixExtractorTests
{
    [Theory]
    [InlineData("cover_1.jpg", "cover")]
    [InlineData("cover (2).tif", "cover")]
    [InlineData("cover.png", "cover")]
    [InlineData("  COVER_1   (2).JPG ", "cover")]
    [InlineData("video_1.mp4", "video")]
    [InlineData("video (2).mov", "video")]
    [InlineData("video.mkv", "video")]
    [InlineData("card_front_1.pdf", "card")]
    [InlineData("  Poster   A3_1.pdf ", "poster")]
    [InlineData("sticker_a4 1.pdf", "sticker")]
    [InlineData("singleword", "singleword")]
    public void ExtractPrefix_ShouldReturnNormalizedSubstringBeforeFirstSpaceOrUnderscore(
        string fileName,
        string expectedPrefix
    )
    {
        var prefix = FilePrefixExtractor.ExtractPrefix(fileName);

        prefix.ShouldBe(expectedPrefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".typoerp_id")]
    [InlineData("_leadingunderscore.pdf")]
    public void ExtractPrefix_WhenNoValidStemBeforeDelimiter_ShouldReturnEmptyString(
        string? fileName
    )
    {
        var prefix = FilePrefixExtractor.ExtractPrefix(fileName);

        prefix.ShouldBe(string.Empty);
    }
}
