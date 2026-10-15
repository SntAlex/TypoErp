namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Элемент фильтра по первичному тегу.
/// </summary>
public sealed record PrimaryTagLookupDto(
    Guid Id,
    string Name,
    string NormalizedName,
    int ProductsCount
);

/// <summary>
///     Элемент фильтра по категории.
/// </summary>
public sealed record CategoryLookupDto(
    Guid Id,
    string Code,
    string? DisplayName,
    bool IsDeleted,
    int ProductsCount
)
{
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(DisplayName) ? Code : $"{DisplayName} ({Code})";
}

/// <summary>
///     Элемент фильтра по формату внутри категории.
/// </summary>
public sealed record FormatLookupDto(
    Guid Id,
    Guid CategoryId,
    string CategoryCode,
    string? CategoryDisplayName,
    string Code,
    bool IsDeleted,
    int ProductsCount
)
{
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(CategoryDisplayName)
            ? $"{Code} [{CategoryCode}]"
            : $"{Code} [{CategoryDisplayName} ({CategoryCode})]";
}

/// <summary>
///     Элемент фильтра по непервичному тегу.
/// </summary>
public sealed record SecondaryTagLookupDto(Guid Id, string Name, int ProductsCount);

/// <summary>
///     Справочные данные для панели фильтрации и сводной статистики раздела «Товары».
/// </summary>
public sealed record ProductCatalogLookups(
    bool IsStorageRootAccessible,
    string StorageRootPath,
    int TotalProductsCount,
    int ValidProductsCount,
    int InvalidProductsCount,
    IReadOnlyList<PrimaryTagLookupDto> PrimaryTags,
    IReadOnlyList<CategoryLookupDto> Categories,
    IReadOnlyList<FormatLookupDto> Formats,
    IReadOnlyList<SecondaryTagLookupDto> SecondaryTags
);
