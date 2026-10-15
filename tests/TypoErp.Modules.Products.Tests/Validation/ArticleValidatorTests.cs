using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Validation;

public sealed class ArticleValidatorTests
{
    private readonly ProductsStorageOptions _options = new() { MaxArticleLength = 60 };

    [Theory]
    [InlineData("  45К   АРТИКУЛ   ТЕСТ ", "45к артикул тест", "45к")]
    [InlineData("a3 постер наруто", "a3 постер наруто", "a3")]
    [InlineData("100шт значки геншин", "100шт значки геншин", "100шт")]
    public void Validate_WhenArticleIsValid_ShouldExtractFormatCodeAndNormalize(
        string input,
        string expectedNormalized,
        string expectedFormatCode
    )
    {
        var result = ArticleValidator.Validate(input, _options);

        result.IsValid.ShouldBeTrue();
        result.NormalizedArticle.ShouldBe(expectedNormalized);
        result.FormatCode.ShouldBe(expectedFormatCode);
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("артикулбезпробела")]
    [InlineData("   однослово   ")]
    public void Validate_WhenArticleHasNoSpaces_ShouldReturnArticleMissingSpaceError(string input)
    {
        var result = ArticleValidator.Validate(input, _options);

        result.IsValid.ShouldBeFalse();
        result.FormatCode.ShouldBeNull();
        result.Errors.ShouldContain(ProductValidationError.ArticleMissingSpace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("45к артикул:тест")]
    [InlineData("45к артикул/тест")]
    [InlineData("45к артикул.")]
    [InlineData("CON")]
    public void Validate_WhenArticleHasInvalidFileSystemName_ShouldReturnInvalidArticleNameError(
        string? input
    )
    {
        var result = ArticleValidator.Validate(input, _options);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.InvalidArticleName);
    }

    [Fact]
    public void Validate_WhenNormalizedArticleExceedsMaxLength_ShouldReturnArticleTooLongError()
    {
        var longArticle = "45к " + new string('а', 57); // 4 + 57 = 61 > 60

        var result = ArticleValidator.Validate(longArticle, _options);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.ArticleTooLong);
    }
}
