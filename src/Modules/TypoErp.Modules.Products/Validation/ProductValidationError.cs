namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Коды ошибок доменной валидации и синхронизации товара.
/// </summary>
public enum ProductValidationError
{
    /// <summary>
    ///     Товар находится вне папки первичного тега (или первичный тег не указан).
    /// </summary>
    MissingPrimaryTag = 1,

    /// <summary>
    ///     Имя первичного тега содержит запрещенные символы ОС или невалидно для файловой системы.
    /// </summary>
    InvalidPrimaryTagName = 2,

    /// <summary>
    ///     Нормализованное имя первичного тега превышает максимально допустимую длину.
    /// </summary>
    PrimaryTagTooLong = 3,

    /// <summary>
    ///     Превышена максимальная вместимость товаров внутри первичного тега.
    /// </summary>
    PrimaryTagCapacityExceeded = 4,

    /// <summary>
    ///     Имя артикула пустое, содержит запрещенные символы ОС или невалидно для файловой системы.
    /// </summary>
    InvalidArticleName = 5,

    /// <summary>
    ///     Нормализованный артикул превышает максимально допустимую длину.
    /// </summary>
    ArticleTooLong = 6,

    /// <summary>
    ///     Нормализованный артикул не содержит ни одного пробела (невозможно выделить формат).
    /// </summary>
    ArticleMissingSpace = 7,

    /// <summary>
    ///     Имя файла товара невалидно для кроссплатформенной файловой системы.
    /// </summary>
    InvalidFileName = 8,

    /// <summary>
    ///     Файл обложки (префикс "cover") имеет недопустимое расширение.
    /// </summary>
    InvalidCoverExtension = 9,

    /// <summary>
    ///     Файл видеообложки (префикс "video") имеет недопустимое расширение.
    /// </summary>
    InvalidVideoExtension = 10,

    /// <summary>
    ///     Не удалось выделить префикс у файла сборки (например, имя начинается с разделителя).
    /// </summary>
    InvalidAssemblyFilePrefix = 11,

    /// <summary>
    ///     В папке товара отсутствуют файлы сборки (черновик товара или только медиафайлы).
    /// </summary>
    MissingAssemblyFiles = 12,

    /// <summary>
    ///     Файлы сборки внутри папки одного товара имеют разные префиксы.
    /// </summary>
    InconsistentAssemblyPrefixes = 13,

    /// <summary>
    ///     Конфликт переименования при автоматической нормализации имен на диске.
    /// </summary>
    NormalizationConflict = 14,

    /// <summary>
    ///     Системный файл .typoerp_id поврежден или содержит невалидный идентификатор.
    /// </summary>
    InvalidProductIdFile = 15,
}
