using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Files;

public sealed class ProductFileClassifierTests
{
    private readonly ProductsStorageOptions _options = new();

    [Theory]
    [InlineData(".typoerp_id")]
    [InlineData("  .typoerp_id  ")]
    [InlineData("Thumbs.db")]
    [InlineData("desktop.ini")]
    [InlineData(".DS_Store")]
    [InlineData(".gitignore")]
    [InlineData("~$temp_doc.docx")]
    public void Classify_WhenFileMatchesIgnoredPatterns_ShouldReturnIgnoredKindWithoutErrors(
        string fileName
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.Ignored);
        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cover_1.jpg", "cover_1.jpg", ".jpg")]
    [InlineData("  Cover_1   (2).JPG ", "cover_1 (2).jpg", ".jpg")]
    [InlineData("cover.png", "cover.png", ".png")]
    [InlineData("cover (3).tif", "cover (3).tif", ".tif")]
    [InlineData("cover_main.webp", "cover_main.webp", ".webp")]
    public void Classify_WhenCoverHasAllowedImageExtension_ShouldReturnValidCover(
        string fileName,
        string expectedNormalized,
        string expectedExtension
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.Cover);
        result.NormalizedFileName.ShouldBe(expectedNormalized);
        result.Prefix.ShouldBe("cover");
        result.Extension.ShouldBe(expectedExtension);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("cover_1.pdf")]
    [InlineData("cover_1.mp4")]
    [InlineData("cover_1")]
    public void Classify_WhenCoverHasDisallowedExtension_ShouldReturnInvalidCoverExtensionError(
        string fileName
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.Cover);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.InvalidCoverExtension);
    }

    [Theory]
    [InlineData("video_1.mp4", "video_1.mp4", ".mp4")]
    [InlineData("  VIDEO   (2).MOV ", "video (2).mov", ".mov")]
    [InlineData("video.mkv", "video.mkv", ".mkv")]
    [InlineData("video_preview.webm", "video_preview.webm", ".webm")]
    public void Classify_WhenVideoHasAllowedVideoExtension_ShouldReturnValidVideoCover(
        string fileName,
        string expectedNormalized,
        string expectedExtension
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.VideoCover);
        result.NormalizedFileName.ShouldBe(expectedNormalized);
        result.Prefix.ShouldBe("video");
        result.Extension.ShouldBe(expectedExtension);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("video_1.jpg")]
    [InlineData("video_1.pdf")]
    [InlineData("video_1")]
    public void Classify_WhenVideoHasDisallowedExtension_ShouldReturnInvalidVideoExtensionError(
        string fileName
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.VideoCover);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.InvalidVideoExtension);
    }

    [Theory]
    [InlineData("card_1.pdf", "card_1.pdf", "card")]
    [InlineData("  POSTER   A3_1.tiff ", "poster a3_1.tiff", "poster")]
    [InlineData("sticker_pack_2.cdr", "sticker_pack_2.cdr", "sticker")]
    public void Classify_WhenFileIsAssemblyFile_ShouldExtractCategoryPrefix(
        string fileName,
        string expectedNormalized,
        string expectedPrefix
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.AssemblyFile);
        result.NormalizedFileName.ShouldBe(expectedNormalized);
        result.Prefix.ShouldBe(expectedPrefix);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("_leadingunderscore.pdf")]
    [InlineData("_only.cdr")]
    public void Classify_WhenAssemblyFileHasEmptyPrefix_ShouldReturnInvalidAssemblyFilePrefixError(
        string fileName
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.Kind.ShouldBe(ProductFileKind.AssemblyFile);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.InvalidAssemblyFilePrefix);
    }

    [Theory]
    [InlineData("card:1.pdf")]
    [InlineData("card?1.pdf")]
    [InlineData("card_1.pdf.")]
    [InlineData("CON.pdf")]
    public void Classify_WhenFileNameIsInvalidForFileSystem_ShouldReturnInvalidFileNameError(
        string fileName
    )
    {
        var result = ProductFileClassifier.Classify(fileName, _options);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.InvalidFileName);
    }
}
