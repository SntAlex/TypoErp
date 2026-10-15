using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Validation;

public sealed class PrimaryTagValidatorTests
{
    private readonly ProductsStorageOptions _options = new()
    {
        MaxPrimaryTagLength = 200,
        MaxProductsPerPrimaryTag = 50,
    };

    [Theory]
    [InlineData("  МоЙ   ТЕг \t ", "мой тег")]
    [InlineData("аниме наклейки 2026", "аниме наклейки 2026")]
    [InlineData("Тег_Без_Пробелов", "тег_без_пробелов")]
    public void Validate_WhenPrimaryTagIsValid_ShouldReturnValidResultAndNormalizedName(
        string input,
        string expectedNormalized
    )
    {
        var result = PrimaryTagValidator.Validate(input, _options, 10);

        result.IsValid.ShouldBeTrue();
        result.NormalizedName.ShouldBe(expectedNormalized);
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Validate_WhenPrimaryTagIsNullOrWhitespace_ShouldReturnMissingPrimaryTagError(
        string? input
    )
    {
        var result = PrimaryTagValidator.Validate(input, _options);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.MissingPrimaryTag);
    }

    [Theory]
    [InlineData("bad/tag")]
    [InlineData("bad:tag")]
    [InlineData("bad*tag")]
    [InlineData("CON")]
    [InlineData("aux.tag")]
    [InlineData("tag.")]
    [InlineData("...")]
    public void Validate_WhenPrimaryTagHasInvalidFileSystemName_ShouldReturnInvalidPrimaryTagNameError(
        string input
    )
    {
        var result = PrimaryTagValidator.Validate(input, _options);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.InvalidPrimaryTagName);
    }

    [Fact]
    public void Validate_WhenNormalizedPrimaryTagExceedsMaxLength_ShouldReturnPrimaryTagTooLongError()
    {
        var longTag = new string('а', 201);

        var result = PrimaryTagValidator.Validate(longTag, _options);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.PrimaryTagTooLong);
    }

    [Fact]
    public void Validate_WhenProductCountExceedsCapacity_ShouldReturnPrimaryTagCapacityExceededError()
    {
        var result = PrimaryTagValidator.Validate("валидный тег", _options, 51);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(ProductValidationError.PrimaryTagCapacityExceeded);
    }
}
