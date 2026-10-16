using Microsoft.EntityFrameworkCore;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Реализация сервиса чтения, поиска и фильтрации каталога товаров.
/// </summary>
public sealed class ProductCatalogQueryService : IProductCatalogQueryService
{
    private readonly ProductsDbContext _dbContext;
    private readonly ProductsStorageOptions _options;
    private readonly SemaphoreSlim _queryLock = new(1, 1);
    private readonly IProductStorage _storage;

    public ProductCatalogQueryService(
        ProductsDbContext dbContext,
        IProductStorage storage,
        ProductsStorageOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storage);

        _dbContext = dbContext;
        _storage = storage;
        _options = options ?? new ProductsStorageOptions();
    }

    public async Task<ProductCatalogLookups> GetFilterLookupsAsync(
        bool includeDeletedCategoriesAndFormats = false,
        CancellationToken cancellationToken = default
    )
    {
        await _queryLock.WaitAsync(cancellationToken);
        try
        {
            var isRootAccessible = await _storage.IsRootAccessibleAsync(cancellationToken);

            var totalCount = await _dbContext.Products.AsNoTracking().CountAsync(cancellationToken);
            var validCount = await _dbContext
                .Products.AsNoTracking()
                .CountAsync(p => p.IsValid, cancellationToken);
            var invalidCount = totalCount - validCount;

            var primaryTags = await _dbContext
                .PrimaryTags.AsNoTracking()
                .OrderBy(t => t.NormalizedName)
                .Select(t => new PrimaryTagLookupDto(
                    t.Id,
                    t.Name,
                    t.NormalizedName,
                    t.Products.Count
                ))
                .ToListAsync(cancellationToken);

            var categoriesQuery = _dbContext.Categories.AsNoTracking();
            if (!includeDeletedCategoriesAndFormats)
                categoriesQuery = categoriesQuery.Where(c => !c.IsDeleted);

            var categories = await categoriesQuery
                .OrderBy(c => c.Code)
                .Select(c => new CategoryLookupDto(
                    c.Id,
                    c.Code,
                    c.DisplayName,
                    c.IsDeleted,
                    c.Products.Count
                ))
                .ToListAsync(cancellationToken);

            var formatsQuery = _dbContext
                .Formats.AsNoTracking()
                .Include(f => f.Category)
                .AsQueryable();
            if (!includeDeletedCategoriesAndFormats)
                formatsQuery = formatsQuery.Where(f => !f.IsDeleted && !f.Category.IsDeleted);

            var formats = await formatsQuery
                .OrderBy(f => f.Code)
                .ThenBy(f => f.Category.Code)
                .Select(f => new FormatLookupDto(
                    f.Id,
                    f.CategoryId,
                    f.Category.Code,
                    f.Category.DisplayName,
                    f.Code,
                    f.IsDeleted,
                    f.Products.Count
                ))
                .ToListAsync(cancellationToken);

            var secondaryTags = await _dbContext
                .SecondaryTags.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new SecondaryTagLookupDto(t.Id, t.Name, t.Products.Count))
                .ToListAsync(cancellationToken);

            return new ProductCatalogLookups(
                isRootAccessible,
                _options.RootPath,
                totalCount,
                validCount,
                invalidCount,
                primaryTags,
                categories,
                formats,
                secondaryTags
            );
        }
        finally
        {
            _queryLock.Release();
        }
    }

    public async Task<ProductCatalogPageResult> QueryProductsAsync(
        ProductCatalogFilter filter,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(filter);

        await _queryLock.WaitAsync(cancellationToken);
        try
        {
            var totalAllCount = await _dbContext
                .Products.AsNoTracking()
                .CountAsync(cancellationToken);
            var totalValidCount = await _dbContext
                .Products.AsNoTracking()
                .CountAsync(p => p.IsValid, cancellationToken);
            var totalInvalidCount = totalAllCount - totalValidCount;

            IQueryable<Product> query = _dbContext
                .Products.AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                    .ThenInclude(c => c!.SecondaryTags)
                .Include(p => p.Format)
                    .ThenInclude(f => f!.SecondaryTags)
                .Include(p => p.SecondaryTags)
                .Include(p => p.Files);

            if (filter.Validity == ProductValidityFilter.ValidOnly)
                query = query.Where(p => p.IsValid);
            else if (filter.Validity == ProductValidityFilter.InvalidOnly)
                query = query.Where(p => !p.IsValid);

            if (filter.PrimaryTagId.HasValue)
                query = query.Where(p => p.PrimaryTagId == filter.PrimaryTagId.Value);

            if (filter.CategoryId.HasValue)
                query = query.Where(p => p.CategoryId == filter.CategoryId.Value);

            if (filter.FormatId.HasValue)
                query = query.Where(p => p.FormatId == filter.FormatId.Value);

            var candidates = await query.ToListAsync(cancellationToken);

            IEnumerable<Product> filtered = candidates;

            // Фильтр по непервичным тегам (учитывает прямые теги товара и унаследованные от категории/формата)
            if (filter.SecondaryTagIds is { Count: > 0 })
            {
                var requiredTagIds = filter.SecondaryTagIds.Distinct().ToList();
                filtered = filtered.Where(p => MatchesAllSecondaryTags(p, requiredTagIds));
            }

            // Фильтр по конкретному коду ошибки валидации
            if (filter.ValidationError.HasValue)
            {
                var targetError = filter.ValidationError.Value;
                filtered = filtered.Where(p => p.ValidationErrors.Contains(targetError));
            }

            // Полнотекстовый поиск (регистронезависимый для кириллицы и латиницы)
            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                var normalizedSearch = StringNormalizer.Normalize(filter.SearchText);
                var rawTrimmedSearch = filter.SearchText.Trim();
                filtered = filtered.Where(p =>
                    MatchesSearch(p, normalizedSearch, rawTrimmedSearch)
                );
            }

            var matchedList = SortProducts(filtered, filter.SortBy, filter.SortDescending).ToList();
            var totalMatchedCount = matchedList.Count;

            var page = Math.Max(0, filter.Page);
            var pageSize = filter.PageSize <= 0 ? 25 : filter.PageSize;

            var pagedEntities = matchedList.Skip(page * pageSize).Take(pageSize).ToList();
            var dtoItems = pagedEntities.Select(MapToDto).ToList();

            return new ProductCatalogPageResult(
                dtoItems,
                totalMatchedCount,
                totalAllCount,
                totalValidCount,
                totalInvalidCount
            );
        }
        finally
        {
            _queryLock.Release();
        }
    }

    public async Task<ProductCatalogItemDto?> GetProductByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default
    )
    {
        if (productId == Guid.Empty)
            return null;

        await _queryLock.WaitAsync(cancellationToken);
        try
        {
            var product = await _dbContext
                .Products.AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                    .ThenInclude(c => c!.SecondaryTags)
                .Include(p => p.Format)
                    .ThenInclude(f => f!.SecondaryTags)
                .Include(p => p.SecondaryTags)
                .Include(p => p.Files)
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

            return product is null ? null : MapToDto(product);
        }
        finally
        {
            _queryLock.Release();
        }
    }

    public async Task<ProductsTaxonomyOverviewDto> GetTaxonomyOverviewAsync(
        bool includeDeleted = true,
        CancellationToken cancellationToken = default
    )
    {
        await _queryLock.WaitAsync(cancellationToken);
        try
        {
            var allCategories = await _dbContext
                .Categories.AsNoTracking()
                .AsSplitQuery()
                .Include(c => c.SecondaryTags)
                .Include(c => c.Products)
                .Include(c => c.Formats)
                    .ThenInclude(f => f.SecondaryTags)
                .Include(c => c.Formats)
                    .ThenInclude(f => f.Products)
                .OrderBy(c => c.IsDeleted)
                .ThenBy(c => c.Code)
                .ToListAsync(cancellationToken);

            var activeCategoriesCount = allCategories.Count(c => !c.IsDeleted);
            var deletedCategoriesCount = allCategories.Count - activeCategoriesCount;

            var allFormats = allCategories.SelectMany(c => c.Formats).ToList();
            var activeFormatsCount = allFormats.Count(f => !f.IsDeleted);
            var deletedFormatsCount = allFormats.Count - activeFormatsCount;

            var filteredCategories = includeDeleted
                ? allCategories
                : allCategories.Where(c => !c.IsDeleted).ToList();

            var categoryDtos = new List<CategoryTreeItemDto>(filteredCategories.Count);

            foreach (var category in filteredCategories)
            {
                var categoryTagNames = category
                    .SecondaryTags.Select(t => t.Name)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                var formatsSource = includeDeleted
                    ? category.Formats
                    : category.Formats.Where(f => !f.IsDeleted);

                var formatDtos = formatsSource
                    .OrderBy(f => f.IsDeleted)
                    .ThenBy(f => f.Code, StringComparer.Ordinal)
                    .Select(f =>
                    {
                        var directFormatTags = f
                            .SecondaryTags.Select(t => t.Name)
                            .OrderBy(n => n, StringComparer.Ordinal)
                            .ToList();
                        var directFormatSet = directFormatTags.ToHashSet(StringComparer.Ordinal);
                        var inheritedFromCategory = categoryTagNames
                            .Where(t => !directFormatSet.Contains(t))
                            .ToList();

                        var fmtTotal = f.Products.Count;
                        var fmtValid = f.Products.Count(p => p.IsValid);

                        return new FormatTreeItemDto(
                            f.Id,
                            f.CategoryId,
                            category.Code,
                            category.DisplayName,
                            f.Code,
                            f.IsDeleted,
                            fmtTotal,
                            fmtValid,
                            fmtTotal - fmtValid,
                            directFormatTags,
                            inheritedFromCategory,
                            f.CreatedAtUtc,
                            f.UpdatedAtUtc
                        );
                    })
                    .ToList();

                var catTotal = category.Products.Count;
                var catValid = category.Products.Count(p => p.IsValid);

                categoryDtos.Add(
                    new CategoryTreeItemDto(
                        category.Id,
                        category.Code,
                        category.DisplayName,
                        category.IsDeleted,
                        catTotal,
                        catValid,
                        catTotal - catValid,
                        categoryTagNames,
                        formatDtos,
                        category.CreatedAtUtc,
                        category.UpdatedAtUtc
                    )
                );
            }

            var primaryTagEntities = await _dbContext
                .PrimaryTags.AsNoTracking()
                .Include(t => t.Products)
                .OrderBy(t => t.NormalizedName)
                .ToListAsync(cancellationToken);

            var primaryTagSummaries = primaryTagEntities
                .Select(t =>
                {
                    var total = t.Products.Count;
                    var valid = t.Products.Count(p => p.IsValid);
                    return new PrimaryTagSummaryDto(
                        t.Id,
                        t.Name,
                        t.NormalizedName,
                        total,
                        valid,
                        total - valid,
                        _options.MaxProductsPerPrimaryTag,
                        t.CreatedAtUtc,
                        t.UpdatedAtUtc
                    );
                })
                .ToList();

            var secondaryTagEntities = await _dbContext
                .SecondaryTags.AsNoTracking()
                .AsSplitQuery()
                .Include(t => t.Products)
                .Include(t => t.Categories)
                .Include(t => t.Formats)
                .OrderBy(t => t.Name)
                .ToListAsync(cancellationToken);

            var secondaryTagSummaries = secondaryTagEntities
                .Select(t => new SecondaryTagSummaryDto(
                    t.Id,
                    t.Name,
                    t.Products.Count,
                    t.Categories.Count,
                    t.Formats.Count,
                    t.CreatedAtUtc
                ))
                .ToList();

            return new ProductsTaxonomyOverviewDto(
                categoryDtos,
                primaryTagSummaries,
                secondaryTagSummaries,
                activeCategoriesCount,
                deletedCategoriesCount,
                activeFormatsCount,
                deletedFormatsCount
            );
        }
        finally
        {
            _queryLock.Release();
        }
    }

    private static bool MatchesAllSecondaryTags(Product product, List<Guid> requiredTagIds)
    {
        foreach (var requiredId in requiredTagIds)
        {
            var hasDirect = product.SecondaryTags.Any(t => t.Id == requiredId);
            var hasFromCategory =
                product.Category?.SecondaryTags.Any(t => t.Id == requiredId) == true;
            var hasFromFormat = product.Format?.SecondaryTags.Any(t => t.Id == requiredId) == true;

            if (!hasDirect && !hasFromCategory && !hasFromFormat)
                return false;
        }

        return true;
    }

    private static bool MatchesSearch(
        Product product,
        string normalizedSearch,
        string rawTrimmedSearch
    )
    {
        if (
            ContainsOrdinalIgnoreCase(product.NormalizedArticle, normalizedSearch)
            || ContainsOrdinalIgnoreCase(product.Article, rawTrimmedSearch)
            || ContainsOrdinalIgnoreCase(product.Id.ToString("D"), rawTrimmedSearch)
        )
            return true;

        if (
            product.PrimaryTag is not null
            && (
                ContainsOrdinalIgnoreCase(product.PrimaryTag.NormalizedName, normalizedSearch)
                || ContainsOrdinalIgnoreCase(product.PrimaryTag.Name, rawTrimmedSearch)
            )
        )
            return true;

        if (product.Category is not null)
        {
            if (ContainsOrdinalIgnoreCase(product.Category.Code, normalizedSearch))
                return true;

            if (
                !string.IsNullOrWhiteSpace(product.Category.DisplayName)
                && (
                    ContainsOrdinalIgnoreCase(
                        StringNormalizer.Normalize(product.Category.DisplayName),
                        normalizedSearch
                    ) || ContainsOrdinalIgnoreCase(product.Category.DisplayName, rawTrimmedSearch)
                )
            )
                return true;
        }

        if (
            product.Format is not null
            && ContainsOrdinalIgnoreCase(product.Format.Code, normalizedSearch)
        )
            return true;

        if (
            product.SecondaryTags.Any(t => ContainsOrdinalIgnoreCase(t.Name, normalizedSearch))
            || product.Category?.SecondaryTags.Any(t =>
                ContainsOrdinalIgnoreCase(t.Name, normalizedSearch)
            ) == true
            || product.Format?.SecondaryTags.Any(t =>
                ContainsOrdinalIgnoreCase(t.Name, normalizedSearch)
            ) == true
        )
            return true;

        if (
            product.Files.Any(f =>
                ContainsOrdinalIgnoreCase(f.NormalizedFileName, normalizedSearch)
                || ContainsOrdinalIgnoreCase(f.FileName, rawTrimmedSearch)
            )
        )
            return true;

        return false;
    }

    private static bool ContainsOrdinalIgnoreCase(string? source, string value)
    {
        return !string.IsNullOrEmpty(source)
            && !string.IsNullOrEmpty(value)
            && source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<Product> SortProducts(
        IEnumerable<Product> products,
        ProductCatalogSortField sortField,
        bool descending
    )
    {
        return (sortField, descending) switch
        {
            (ProductCatalogSortField.PrimaryTag, false) => products
                .OrderBy(p => p.PrimaryTag?.NormalizedName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),
            (ProductCatalogSortField.PrimaryTag, true) => products
                .OrderByDescending(
                    p => p.PrimaryTag?.NormalizedName ?? string.Empty,
                    StringComparer.Ordinal
                )
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),

            (ProductCatalogSortField.Category, false) => products
                .OrderBy(p => p.Category?.Code ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),
            (ProductCatalogSortField.Category, true) => products
                .OrderByDescending(p => p.Category?.Code ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),

            (ProductCatalogSortField.Format, false) => products
                .OrderBy(p => p.Format?.Code ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),
            (ProductCatalogSortField.Format, true) => products
                .OrderByDescending(p => p.Format?.Code ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),

            (ProductCatalogSortField.Validity, false) => products
                .OrderBy(p => p.IsValid)
                .ThenByDescending(p => p.ValidationErrors.Count)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),
            (ProductCatalogSortField.Validity, true) => products
                .OrderByDescending(p => p.IsValid)
                .ThenBy(p => p.ValidationErrors.Count)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),

            (ProductCatalogSortField.FilesCount, false) => products
                .OrderBy(p => p.Files.Count)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),
            (ProductCatalogSortField.FilesCount, true) => products
                .OrderByDescending(p => p.Files.Count)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),

            (ProductCatalogSortField.UpdatedAtUtc, false) => products
                .OrderBy(p => p.UpdatedAtUtc)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),
            (ProductCatalogSortField.UpdatedAtUtc, true) => products
                .OrderByDescending(p => p.UpdatedAtUtc)
                .ThenBy(p => p.NormalizedArticle, StringComparer.Ordinal),

            (_, true) => products
                .OrderByDescending(p => p.NormalizedArticle, StringComparer.Ordinal)
                .ThenBy(p => p.Id),
            _ => products
                .OrderBy(p => p.NormalizedArticle, StringComparer.Ordinal)
                .ThenBy(p => p.Id),
        };
    }

    private static ProductCatalogItemDto MapToDto(Product product)
    {
        var directTags = product
            .SecondaryTags.Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var directSet = directTags.ToHashSet(StringComparer.Ordinal);
        var inheritedTags = new HashSet<string>(StringComparer.Ordinal);

        if (product.Category?.SecondaryTags is not null)
        {
            foreach (var tag in product.Category.SecondaryTags)
                if (!directSet.Contains(tag.Name))
                    inheritedTags.Add(tag.Name);
        }

        if (product.Format?.SecondaryTags is not null)
        {
            foreach (var tag in product.Format.SecondaryTags)
                if (!directSet.Contains(tag.Name))
                    inheritedTags.Add(tag.Name);
        }

        var files = product
            .Files.OrderBy(f => f.Kind)
            .ThenBy(f => f.NormalizedFileName, StringComparer.Ordinal)
            .Select(f => new ProductCatalogFileDto(
                f.Id,
                f.FileName,
                f.NormalizedFileName,
                f.Kind,
                f.Prefix,
                f.Extension,
                f.SizeBytes,
                f.XxHash64,
                f.UpdatedAtUtc
            ))
            .ToList();

        var coversCount = files.Count(f => f.Kind == ProductFileKind.Cover);
        var videoCoversCount = files.Count(f => f.Kind == ProductFileKind.VideoCover);
        var assemblyFilesCount = files.Count(f => f.Kind == ProductFileKind.AssemblyFile);

        return new ProductCatalogItemDto(
            product.Id,
            product.Article,
            product.NormalizedArticle,
            product.PrimaryTagId,
            product.PrimaryTag?.Name,
            product.CategoryId,
            product.Category?.Code,
            product.Category?.DisplayName,
            product.FormatId,
            product.Format?.Code,
            product.IsValid,
            product.ValidationErrors.ToList(),
            directTags,
            inheritedTags.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            files,
            coversCount,
            videoCoversCount,
            assemblyFilesCount,
            product.CreatedAtUtc,
            product.UpdatedAtUtc
        );
    }
}
