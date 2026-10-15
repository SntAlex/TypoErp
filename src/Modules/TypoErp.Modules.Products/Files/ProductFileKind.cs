namespace TypoErp.Modules.Products.Files;

/// <summary>
///     Классификация файла внутри папки товара.
/// </summary>
public enum ProductFileKind
{
    /// <summary>
    ///     Системный или игнорируемый файл (.typoerp_id, Thumbs.db, .DS_Store и т.д.).
    /// </summary>
    Ignored = 0,

    /// <summary>
    ///     Обложка товара (префикс "cover").
    /// </summary>
    Cover = 1,

    /// <summary>
    ///     Видеообложка товара (префикс "video").
    /// </summary>
    VideoCover = 2,

    /// <summary>
    ///     Файл сборки товара (определяет категорию товара по единому префиксу).
    /// </summary>
    AssemblyFile = 3,
}
