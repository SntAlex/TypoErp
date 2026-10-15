using System.IO.Enumeration;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Validation;
using TypoErp.SharedKernel.Files;
using TypoErp.SharedKernel.Normalization;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.Modules.Products.Files;

/// <summary>
///     Классифицирует файлы товара (Ignored, Cover, VideoCover, AssemblyFile)
///     и проверяет допустимость их имен, префиксов и расширений.
/// </summary>
public static class ProductFileClassifier
{
    public const string CoverPrefix = "cover";
    public const string VideoPrefix = "video";

    public static ClassifiedProductFile Classify(
        string fileName,
        ProductsStorageOptions? options = null
    )
    {
        var opts = options ?? new ProductsStorageOptions();
        var trimmed = fileName?.Trim() ?? string.Empty;
        var normalizedFileName = StringNormalizer.NormalizeFileName(fileName);
        var extension = ExtractExtension(normalizedFileName);

        if (IsIgnoredFile(trimmed, normalizedFileName, opts))
            return new ClassifiedProductFile(
                fileName ?? string.Empty,
                normalizedFileName,
                ProductFileKind.Ignored,
                string.Empty,
                extension,
                []
            );

        var errors = new List<ProductValidationError>();

        if (
            string.IsNullOrEmpty(normalizedFileName)
            || trimmed.EndsWith('.')
            || !FileSystemNameValidator.IsValid(normalizedFileName)
        )
            errors.Add(ProductValidationError.InvalidFileName);

        var prefix = FilePrefixExtractor.ExtractPrefix(normalizedFileName);

        if (string.Equals(prefix, CoverPrefix, StringComparison.Ordinal))
        {
            if (!IsExtensionAllowed(extension, opts.AllowedImageExtensions))
                errors.Add(ProductValidationError.InvalidCoverExtension);

            return new ClassifiedProductFile(
                fileName ?? string.Empty,
                normalizedFileName,
                ProductFileKind.Cover,
                prefix,
                extension,
                errors
            );
        }

        if (string.Equals(prefix, VideoPrefix, StringComparison.Ordinal))
        {
            if (!IsExtensionAllowed(extension, opts.AllowedVideoExtensions))
                errors.Add(ProductValidationError.InvalidVideoExtension);

            return new ClassifiedProductFile(
                fileName ?? string.Empty,
                normalizedFileName,
                ProductFileKind.VideoCover,
                prefix,
                extension,
                errors
            );
        }

        if (string.IsNullOrEmpty(prefix) || !FileSystemNameValidator.IsValid(prefix))
            errors.Add(ProductValidationError.InvalidAssemblyFilePrefix);

        return new ClassifiedProductFile(
            fileName ?? string.Empty,
            normalizedFileName,
            ProductFileKind.AssemblyFile,
            prefix,
            extension,
            errors
        );
    }

    public static IReadOnlyList<ClassifiedProductFile> ClassifyMany(
        IEnumerable<string>? fileNames,
        ProductsStorageOptions? options = null
    )
    {
        if (fileNames is null)
            return [];

        var opts = options ?? new ProductsStorageOptions();
        var results = new List<ClassifiedProductFile>();

        foreach (var fileName in fileNames)
            results.Add(Classify(fileName, opts));

        return results;
    }

    private static string ExtractExtension(string normalizedFileName)
    {
        if (string.IsNullOrEmpty(normalizedFileName) || normalizedFileName.EndsWith('.'))
            return string.Empty;

        var lastDotIndex = normalizedFileName.LastIndexOf('.');
        return lastDotIndex >= 0 ? normalizedFileName[lastDotIndex..] : string.Empty;
    }

    private static bool IsIgnoredFile(
        string trimmedFileName,
        string normalizedFileName,
        ProductsStorageOptions options
    )
    {
        if (string.IsNullOrEmpty(trimmedFileName) || IsAllDots(trimmedFileName))
            return false;

        if (
            !string.IsNullOrWhiteSpace(options.ProductIdFileName)
            && (
                string.Equals(
                    trimmedFileName,
                    options.ProductIdFileName.Trim(),
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    normalizedFileName,
                    options.ProductIdFileName.Trim(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
            return true;

        foreach (var rawPattern in options.IgnoredFilePatterns)
        {
            if (string.IsNullOrWhiteSpace(rawPattern))
                continue;

            var pattern = rawPattern.Trim();

            if (
                FileSystemName.MatchesSimpleExpression(pattern, trimmedFileName, true)
                || (
                    !string.IsNullOrEmpty(normalizedFileName)
                    && FileSystemName.MatchesSimpleExpression(pattern, normalizedFileName, true)
                )
            )
                return true;
        }

        return false;
    }

    private static bool IsExtensionAllowed(string extension, string[] allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(extension) || allowedExtensions.Length == 0)
            return false;

        var normalizedExt = extension.StartsWith('.') ? extension : "." + extension;

        foreach (var allowed in allowedExtensions)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;

            var trimmedAllowed = allowed.Trim();
            var normalizedAllowed = trimmedAllowed.StartsWith('.')
                ? trimmedAllowed
                : "." + trimmedAllowed;

            if (string.Equals(normalizedExt, normalizedAllowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsAllDots(ReadOnlySpan<char> value)
    {
        foreach (var ch in value)
            if (ch != '.')
                return false;

        return true;
    }
}
