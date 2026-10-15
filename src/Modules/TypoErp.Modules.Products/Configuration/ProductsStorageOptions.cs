namespace TypoErp.Modules.Products.Configuration;

/// <summary>
///     Настройки файлового хранилища и правил валидации модуля «Товары».
/// </summary>
public sealed class ProductsStorageOptions
{
    public const string SectionName = "ProductsStorage";
    public const string DefaultProductIdFileName = ".typoerp_id";

    /// <summary>
    ///     Корневой путь хранилища товаров на диске.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    ///     Имя системного файла внутри папки товара, хранящего уникальный Guid товара.
    /// </summary>
    public string ProductIdFileName { get; set; } = DefaultProductIdFileName;

    /// <summary>
    ///     Максимальная длина нормализованного названия первичного тега.
    /// </summary>
    public int MaxPrimaryTagLength { get; set; } = 200;

    /// <summary>
    ///     Максимальное количество товаров внутри одного первичного тега.
    /// </summary>
    public int MaxProductsPerPrimaryTag { get; set; } = 1000;

    /// <summary>
    ///     Максимальная длина нормализованного артикула товара.
    /// </summary>
    public int MaxArticleLength { get; set; } = 60;

    /// <summary>
    ///     Разрешенные расширения файлов обложек (префикс "cover").
    /// </summary>
    public string[] AllowedImageExtensions { get; set; } =
    [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"];

    /// <summary>
    ///     Разрешенные расширения файлов видеообложек (префикс "video").
    /// </summary>
    public string[] AllowedVideoExtensions { get; set; } =
    [".mp4", ".mov", ".mkv", ".avi", ".webm"];

    /// <summary>
    ///     Список игнорируемых имен файлов и масок ОС, которые не участвуют в валидации сборки.
    /// </summary>
    public string[] IgnoredFilePatterns { get; set; } =
    [DefaultProductIdFileName, "thumbs.db", "desktop.ini", ".ds_store", "~$*", ".*"];
}
