namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Элемент дерева формата внутри конкретной категории товаров.
/// </summary>
public sealed record FormatTreeItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryCode,
    string? CategoryDisplayName,
    string Code,
    bool IsDeleted,
    int TotalProductsCount,
    int ValidProductsCount,
    int InvalidProductsCount,
    IReadOnlyList<string> SecondaryTags,
    IReadOnlyList<string> InheritedCategorySecondaryTags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
)
{
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(CategoryDisplayName)
            ? $"{Code} [{CategoryCode}]"
            : $"{Code} [{CategoryDisplayName} ({CategoryCode})]";
}

/// <summary>
///     Узел категории в дереве «Категория -> Форматы» со списком дочерних форматов,
///     пользовательским названием (<see cref="DisplayName" />), тегами и статусом мягкого удаления (<see cref="IsDeleted" />).
/// </summary>
public sealed record CategoryTreeItemDto(
    Guid Id,
    string Code,
    string? DisplayName,
    bool IsDeleted,
    int TotalProductsCount,
    int ValidProductsCount,
    int InvalidProductsCount,
    IReadOnlyList<string> SecondaryTags,
    IReadOnlyList<FormatTreeItemDto> Formats,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
)
{
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(DisplayName) ? Code : $"{DisplayName} ({Code})";
}

/// <summary>
///     Сводная информация о папке первичного тега 1-го уровня для управления тегами.
/// </summary>
public sealed record PrimaryTagSummaryDto(
    Guid Id,
    string Name,
    string NormalizedName,
    int TotalProductsCount,
    int ValidProductsCount,
    int InvalidProductsCount,
    int MaxCapacity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

/// <summary>
///     Сводная информация о непервичном теге и его использовании на товарах, категориях и форматах.
/// </summary>
public sealed record SecondaryTagSummaryDto(
    Guid Id,
    string Name,
    int DirectProductsCount,
    int CategoriesCount,
    int FormatsCount,
    DateTimeOffset CreatedAtUtc
);

/// <summary>
///     Полный срез данных для вкладки «Категории, Форматы и Теги».
/// </summary>
public sealed record ProductsTaxonomyOverviewDto(
    IReadOnlyList<CategoryTreeItemDto> Categories,
    IReadOnlyList<PrimaryTagSummaryDto> PrimaryTags,
    IReadOnlyList<SecondaryTagSummaryDto> SecondaryTags,
    int ActiveCategoriesCount,
    int DeletedCategoriesCount,
    int ActiveFormatsCount,
    int DeletedFormatsCount
);
