using TypoErp.Modules.Products.Validation;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Entities;

/// <summary>
///     Сущность товара в PostgreSQL. Отражает как валидные товары, так и черновики/проблемные товары с диска.
/// </summary>
public sealed class Product
{
    private Product() { }

    public Product(
        Guid id,
        string article,
        Guid? primaryTagId,
        Guid? categoryId,
        Guid? formatId,
        DateTimeOffset createdAtUtc,
        IEnumerable<ProductValidationError>? validationErrors = null
    )
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Product Id cannot be Guid.Empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(article);

        Id = id;
        Article = article;
        NormalizedArticle = StringNormalizer.Normalize(article);
        PrimaryTagId = primaryTagId;
        CategoryId = categoryId;
        FormatId = formatId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;

        SetValidationErrors(validationErrors);
    }

    /// <summary>
    ///     Уникальный идентификатор товара (совпадает с Guid внутри файла .typoerp_id).
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     Фактическое имя папки артикула на диске (в штатном режиме совпадает с <see cref="NormalizedArticle" />).
    /// </summary>
    public string Article { get; set; } = string.Empty;

    /// <summary>
    ///     Нормализованный артикул товара.
    /// </summary>
    public string NormalizedArticle { get; set; } = string.Empty;

    public Guid? PrimaryTagId { get; set; }

    public PrimaryTag? PrimaryTag { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid? FormatId { get; set; }

    public Format? Format { get; set; }

    /// <summary>
    ///     Признак полной валидности товара. Равен true, если список <see cref="ValidationErrors" /> пуст.
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    ///     Список кодов ошибок валидации и синхронизации товара.
    /// </summary>
    public List<ProductValidationError> ValidationErrors { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<ProductFile> Files { get; set; } = new List<ProductFile>();

    public ICollection<SecondaryTag> SecondaryTags { get; set; } = new List<SecondaryTag>();

    public void SetValidationErrors(IEnumerable<ProductValidationError>? errors)
    {
        ValidationErrors = errors?.Distinct().ToList() ?? [];
        IsValid = ValidationErrors.Count == 0;
    }

    public void UpdateArticle(string article, DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(article);
        Article = article;
        NormalizedArticle = StringNormalizer.Normalize(article);
        UpdatedAtUtc = updatedAtUtc;
    }
}
