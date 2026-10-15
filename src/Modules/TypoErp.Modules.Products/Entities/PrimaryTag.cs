using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Entities;

/// <summary>
///     Сущность первичного тега (папка 1-го уровня в корне хранилища товаров).
/// </summary>
public sealed class PrimaryTag
{
    private PrimaryTag() { }

    public PrimaryTag(string name, DateTimeOffset createdAtUtc, Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id ?? Guid.NewGuid();
        Name = name;
        NormalizedName = StringNormalizer.Normalize(name);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; set; }

    /// <summary>
    ///     Фактическое имя папки первичного тега на диске (в штатном режиме совпадает с <see cref="NormalizedName" />).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Нормализованное название первичного тега.
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public void UpdateName(string name, DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        NormalizedName = StringNormalizer.Normalize(name);
        UpdatedAtUtc = updatedAtUtc;
    }
}
