using System.Text;
using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Management;
using TypoErp.Modules.Products.Management.Preview;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Management;

public sealed class ProductDraftInspectorTests
{
    private readonly ProductsStorageOptions _options = new()
    {
        MaxPrimaryTagLength = 200,
        MaxArticleLength = 60,
        MaxProductsPerPrimaryTag = 1000,
    };

    [Fact]
    public void Inspect_WhenEmptyFiles_ShouldReturnDraftWithMissingAssemblyFilesAndExtractedFormat()
    {
        var result = ProductDraftInspector.Inspect(
            primaryTagName: "  Сезон   Осень  ",
            article: "  45К   Наклейка   Котики ",
            existingFiles: [],
            uploads: [],
            options: _options
        );

        result.NormalizedPrimaryTag.ShouldBe("сезон осень");
        result.NormalizedArticle.ShouldBe("45к наклейка котики");
        result.FormatCode.ShouldBe("45к");
        result.CategoryCode.ShouldBeNull();
        result.IsValid.ShouldBeFalse();
        result.IsDraftOnly.ShouldBeTrue();
        result.ValidationErrors.ShouldBe([ProductValidationError.MissingAssemblyFiles]);
    }

    [Fact]
    public void Inspect_WhenUploadedFileMatchesExistingByXxHash64_ShouldMarkUnchangedByHash()
    {
        var existingBytes = Encoding.UTF8.GetBytes("print-ready-pdf-content");
        var existingHash = ProductFileHasher.Compute(existingBytes);

        var existingFiles = new[]
        {
            new ProductDraftExistingFileInput(
                CurrentFileName: "card_1.pdf",
                NewFileName: "card_1.pdf",
                SizeBytes: existingHash.SizeBytes,
                XxHash64: existingHash.HashHex,
                IsMarkedForDeletion: false
            ),
        };

        var uploads = new[]
        {
            new ProductFileUpload("  CARD_1.PDF ", existingBytes),
            new ProductFileUpload("cover_1.jpg", [1, 2, 3, 4]),
        };

        var result = ProductDraftInspector.Inspect(
            primaryTagName: "тег 1",
            article: "45к товар 1",
            existingFiles: existingFiles,
            uploads: uploads,
            options: _options
        );

        result.IsValid.ShouldBeTrue();
        result.CategoryCode.ShouldBe("card");
        result.FormatCode.ShouldBe("45к");
        result.UnchangedUploadsCount.ShouldBe(1);
        result.ChangedOrNewUploadsCount.ShouldBe(1);

        var cardPreview = result.Files.Single(f => f.EffectiveNormalizedFileName == "card_1.pdf");
        cardPreview.DiffStatus.ShouldBe(ProductDraftFileDiffStatus.UnchangedByHash);
        cardPreview.Kind.ShouldBe(ProductFileKind.AssemblyFile);

        var coverPreview = result.Files.Single(f => f.EffectiveNormalizedFileName == "cover_1.jpg");
        coverPreview.DiffStatus.ShouldBe(ProductDraftFileDiffStatus.NewFile);
        coverPreview.Kind.ShouldBe(ProductFileKind.Cover);
    }

    [Fact]
    public void Inspect_WhenUploadedFileModifiesExistingContent_ShouldMarkContentModified()
    {
        var oldBytes = Encoding.UTF8.GetBytes("old-version");
        var newBytes = Encoding.UTF8.GetBytes("new-version-updated");
        var oldHash = ProductFileHasher.Compute(oldBytes);

        var existingFiles = new[]
        {
            new ProductDraftExistingFileInput(
                CurrentFileName: "card_1.pdf",
                NewFileName: "card_1.pdf",
                SizeBytes: oldHash.SizeBytes,
                XxHash64: oldHash.HashHex,
                IsMarkedForDeletion: false
            ),
        };

        var uploads = new[] { new ProductFileUpload("card_1.pdf", newBytes) };

        var result = ProductDraftInspector.Inspect(
            primaryTagName: "тег 1",
            article: "45к товар 1",
            existingFiles: existingFiles,
            uploads: uploads,
            options: _options
        );

        result.IsValid.ShouldBeTrue();
        result.ChangedOrNewUploadsCount.ShouldBe(1);
        result.UnchangedUploadsCount.ShouldBe(0);

        var filePreview = result.Files.Single();
        filePreview.DiffStatus.ShouldBe(ProductDraftFileDiffStatus.ContentModified);
    }

    [Fact]
    public void Inspect_WhenConflictingAssemblyPrefixesAreRenamedOrDeleted_ShouldBecomeValid()
    {
        var existingFiles = new[]
        {
            new ProductDraftExistingFileInput(
                CurrentFileName: "card_front.pdf",
                NewFileName: "card_front.pdf",
                SizeBytes: 100,
                XxHash64: "0000000000000001",
                IsMarkedForDeletion: false
            ),
            new ProductDraftExistingFileInput(
                CurrentFileName: "poster_back.pdf",
                NewFileName: "card_back.pdf",
                SizeBytes: 120,
                XxHash64: "0000000000000002",
                IsMarkedForDeletion: false
            ),
            new ProductDraftExistingFileInput(
                CurrentFileName: "wrong_extra.pdf",
                NewFileName: "wrong_extra.pdf",
                SizeBytes: 50,
                XxHash64: "0000000000000003",
                IsMarkedForDeletion: true
            ),
        };

        var result = ProductDraftInspector.Inspect(
            primaryTagName: "тег починки",
            article: "10х15 визитка тест",
            existingFiles: existingFiles,
            uploads: [],
            options: _options
        );

        result.IsValid.ShouldBeTrue();
        result.CategoryCode.ShouldBe("card");
        result.FormatCode.ShouldBe("10х15");
        result.ValidationErrors.ShouldBeEmpty();
        result.Files.Count(f => !f.IsMarkedForDeletion).ShouldBe(2);
    }

    [Fact]
    public void Inspect_WhenTwoExistingFilesRenamedToSameNormalizedName_ShouldReportDuplicateTargetError()
    {
        var existingFiles = new[]
        {
            new ProductDraftExistingFileInput(
                CurrentFileName: "card_1.pdf",
                NewFileName: "card_final.pdf",
                SizeBytes: 100,
                XxHash64: "0000000000000001",
                IsMarkedForDeletion: false
            ),
            new ProductDraftExistingFileInput(
                CurrentFileName: "card_2.pdf",
                NewFileName: "  CARD_FINAL.PDF ",
                SizeBytes: 200,
                XxHash64: "0000000000000002",
                IsMarkedForDeletion: false
            ),
        };

        var result = ProductDraftInspector.Inspect(
            primaryTagName: "тег 1",
            article: "45к товар",
            existingFiles: existingFiles,
            uploads: [],
            options: _options
        );

        result.HasDuplicateTargetFileNames.ShouldBeTrue();
        result.IsValid.ShouldBeFalse();
        result.ValidationErrors.ShouldContain(ProductValidationError.NormalizationConflict);
    }
}
