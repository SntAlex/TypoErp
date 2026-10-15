using Shouldly;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Validation;

public sealed class ProductValidationErrorDescriptorTests
{
    [Fact]
    public void AllValidationErrors_ShouldHaveNonEmptyTitleAndDescription()
    {
        foreach (var error in Enum.GetValues<ProductValidationError>())
        {
            var title = ProductValidationErrorDescriptor.GetShortTitle(error);
            var description = ProductValidationErrorDescriptor.GetDescription(error);

            title.ShouldNotBeNullOrWhiteSpace($"Missing short title for {error}");
            description.ShouldNotBeNullOrWhiteSpace($"Missing description for {error}");
        }
    }

    [Fact]
    public void IsDraftOnly_ShouldReturnTrue_OnlyWhenSingleErrorIsMissingAssemblyFiles()
    {
        ProductValidationErrorDescriptor
            .IsDraftOnly([ProductValidationError.MissingAssemblyFiles])
            .ShouldBeTrue();

        ProductValidationErrorDescriptor.IsDraftOnly([]).ShouldBeFalse();

        ProductValidationErrorDescriptor
            .IsDraftOnly([
                ProductValidationError.MissingAssemblyFiles,
                ProductValidationError.ArticleMissingSpace,
            ])
            .ShouldBeFalse();

        ProductValidationErrorDescriptor
            .IsDraftOnly([ProductValidationError.NormalizationConflict])
            .ShouldBeFalse();
    }
}
