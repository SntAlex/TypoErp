using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Entities;

/// <summary>
///     Формат товара внутри конкретной категории (выводится из артикула до первого пробела).
///     Уникальность определяется парой (<see cref="CategoryId" />, <see cref="Code" />).
/// </summary>
public sealed class Format
{
    private Format() { }

    public Format(Guid categoryId, string code, DateTimeOffset createdAtUtc, Guid? id = null)
    {
        if (categoryId == Guid.Empty)
            throw new ArgumentException("CategoryId cannot be Guid.Empty.", nameof(categoryId));

        var normalizedCode = StringNormalizer.Normalize(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedCode);

        Id = id ?? Guid.NewGuid();
        CategoryId = categoryId;
        Code = normalizedCode;
        IsDeleted = false;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>
    ///     Нормализованный код формата (подстрока артикула до первого пробела, например "45к").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    ///     Флаг мягкого удаления (true, если к формату больше не привязано ни одного товара).
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public ICollection<SecondaryTag> SecondaryTags { get; set; } = new List<SecondaryTag>();

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
