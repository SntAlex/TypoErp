using TypoErp.Modules.Products.Files;

namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Метаданные файла товара для детального просмотра в таблице товаров.
/// </summary>
public sealed record ProductCatalogFileDto(
    Guid Id,
    string FileName,
    string NormalizedFileName,
    ProductFileKind Kind,
    string Prefix,
    string Extension,
    long SizeBytes,
    string XxHash64,
    DateTimeOffset UpdatedAtUtc
);
