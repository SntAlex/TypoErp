using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Validation;

public sealed class ProductValidatorTests
{
    private readonly ProductsStorageOptions _options = new();

    [Fact]
    public void Validate_WhenProductIsCompletelyValid_ShouldDeriveCategoryAndFormatAndReturnValid()
    {
        string[] files =
        [
            ".typoerp_id",
            "Thumbs.db",
            "  Cover_1.JPG ",
            "cover (2).png",
            "VIDEO_1.MP4",
            "CARD_front_1.pdf",
            "card_back_2.pdf",
        ];

        var result = ProductValidator.Validate(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто ",
            files,
            _options
        );

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        result.NormalizedPrimaryTag.ShouldBe("аниме тетради");
        result.NormalizedArticle.ShouldBe("45к тетрадь наруто");
        result.CategoryCode.ShouldBe("card");
        result.FormatCode.ShouldBe("45к");
    }

    [Fact]
    public void Validate_WhenDraftProductHasNoAssemblyFiles_ShouldReturnMissingAssemblyFilesError()
    {
        string[] files = [".typoerp_id", "cover_1.jpg", "video_1.mp4"];

        var result = ProductValidator.Validate("аниме", "45к черновик товара", files, _options);

        result.IsValid.ShouldBeFalse();
        result.CategoryCode.ShouldBeNull();
        result.FormatCode.ShouldBe("45к");
        result.Errors.ShouldContain(ProductValidationError.MissingAssemblyFiles);
    }

    [Fact]
    public void Validate_WhenAssemblyFilesHaveDifferentPrefixes_ShouldReturnInconsistentAssemblyPrefixesError()
    {
        string[] files = [".typoerp_id", "cover_1.jpg", "card_1.pdf", "poster_2.pdf"];

        var result = ProductValidator.Validate("аниме", "45к конфликт префиксов", files, _options);

        result.IsValid.ShouldBeFalse();
        result.CategoryCode.ShouldBeNull();
        result.Errors.ShouldContain(ProductValidationError.InconsistentAssemblyPrefixes);
    }

    [Fact]
    public void Validate_WhenMultipleErrorsExist_ShouldAggregateAllUniqueValidationErrors()
    {
        string[] files =
        [
            "cover_1.pdf", // InvalidCoverExtension
            "video_1.jpg", // InvalidVideoExtension
            "card_1.pdf",
            "sticker_1.pdf", // InconsistentAssemblyPrefixes
        ];

        var result = ProductValidator.Validate(
            "тег",
            "артикулбезпробела", // ArticleMissingSpace
            files,
            _options,
            additionalErrors: [ProductValidationError.NormalizationConflict]
        );

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.ArticleMissingSpace);
        result.Errors.ShouldContain(ProductValidationError.InvalidCoverExtension);
        result.Errors.ShouldContain(ProductValidationError.InvalidVideoExtension);
        result.Errors.ShouldContain(ProductValidationError.InconsistentAssemblyPrefixes);
        result.Errors.ShouldContain(ProductValidationError.NormalizationConflict);
    }
}
