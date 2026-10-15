using TypoErp.Modules.Products.Files;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Entities;

/// <summary>
///     Метаданные файла товара в PostgreSQL (с размером и 64-битным хэшем XxHash64 для diff-синхронизации).
/// </summary>
public sealed class ProductFile
{
    private ProductFile() { }

    public ProductFile(
        Guid productId,
        string fileName,
        ProductFileKind kind,
        string prefix,
        string extension,
        long sizeBytes,
        string xxHash64,
        DateTimeOffset updatedAtUtc,
        Guid? id = null
    )
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId cannot be Guid.Empty.", nameof(productId));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(xxHash64);

        Id = id ?? Guid.NewGuid();
        ProductId = productId;
        FileName = fileName;
        NormalizedFileName = StringNormalizer.NormalizeFileName(fileName);
        Kind = kind;
        Prefix = prefix ?? string.Empty;
        Extension = extension ?? string.Empty;
        SizeBytes = sizeBytes;
        XxHash64 = xxHash64;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    ///     Фактическое имя файла на диске (в штатном режиме совпадает с <see cref="NormalizedFileName" />).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    ///     Нормализованное имя файла.
    /// </summary>
    public string NormalizedFileName { get; set; } = string.Empty;

    public ProductFileKind Kind { get; set; }

    public string Prefix { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>
    ///     64-битный хэш XxHash64 в виде 16-символьной hex-строки.
    /// </summary>
    public string XxHash64 { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
