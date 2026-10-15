using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Entities;

/// <summary>
///     Категория товаров, автоматически выводимая из единого префикса файлов сборки.
///     Поддерживает пользовательское название (<see cref="DisplayName" />) и мягкое удаление (<see cref="IsDeleted" />).
/// </summary>
public sealed class Category
{
    private Category() { }

    public Category(
        string code,
        DateTimeOffset createdAtUtc,
        string? displayName = null,
        Guid? id = null
    )
    {
        var normalizedCode = StringNormalizer.Normalize(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedCode);

        Id = id ?? Guid.NewGuid();
        Code = normalizedCode;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        IsDeleted = false;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; set; }

    /// <summary>
    ///     Уникальный нормализованный код категории (префикс файлов сборки, например "card").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    ///     Отображаемое пользовательское название категории (например, "Наклейки").
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    ///     Флаг мягкого удаления (true, если к категории больше не привязано ни одного товара).
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Format> Formats { get; set; } = new List<Format>();

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public ICollection<SecondaryTag> SecondaryTags { get; set; } = new List<SecondaryTag>();

    public void UpdateDisplayName(string? displayName, DateTimeOffset updatedAtUtc)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SoftDelete(DateTimeOffset updatedAtUtc)
    {
        IsDeleted = true;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Restore(DateTimeOffset updatedAtUtc)
    {
        IsDeleted = false;
        UpdatedAtUtc = updatedAtUtc;
    }
}
