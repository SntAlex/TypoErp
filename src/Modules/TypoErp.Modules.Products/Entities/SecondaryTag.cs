using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Entities;

/// <summary>
///     Непервичный тег в PostgreSQL. Может назначаться на товары, категории и форматы.
/// </summary>
public sealed class SecondaryTag
{
    private SecondaryTag() { }

    public SecondaryTag(string name, DateTimeOffset createdAtUtc, Guid? id = null)
    {
        var normalizedName = StringNormalizer.Normalize(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        Id = id ?? Guid.NewGuid();
        Name = normalizedName;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; set; }

    /// <summary>
    ///     Уникальное нормализованное имя непервичного тега.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();

    public ICollection<Category> Categories { get; set; } = new List<Category>();

    public ICollection<Format> Formats { get; set; } = new List<Format>();
}
