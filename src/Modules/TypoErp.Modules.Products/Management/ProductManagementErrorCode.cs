namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Коды ошибок бизнес-операций в сервисе управления товарами (<see cref="IProductManagementService" />).
/// </summary>
public enum ProductManagementErrorCode
{
    /// <summary>
    ///     Запрошенный товар, категория, формат или тег не найдены в БД.
    /// </summary>
    NotFound = 1,

    /// <summary>
    ///     Название первичного тега пустое, слишком длинное или содержит запрещенные символы ФС.
    /// </summary>
    InvalidPrimaryTag = 2,

    /// <summary>
    ///     Превышена максимальная вместимость товаров внутри первичного тега.
    /// </summary>
    PrimaryTagCapacityExceeded = 3,

    /// <summary>
    ///     Артикул пустой, не содержит пробела, превышает лимит длины или невалиден для ФС.
    /// </summary>
    InvalidArticle = 4,

    /// <summary>
    ///     Имя файла невалидно для кроссплатформенной ФС или относится к системным/игнорируемым файлам.
    /// </summary>
    InvalidFileName = 5,

    /// <summary>
    ///     В одном запросе переданы файлы, которые после нормализации имеют одинаковое имя.
    /// </summary>
    DuplicateFileNamesInRequest = 6,

    /// <summary>
    ///     Целевая папка (первичный тег / артикул), файл или тег с таким именем уже существуют.
    /// </summary>
    TargetAlreadyExists = 7,

    /// <summary>
    ///     Название непервичного тега пустое или превышает допустимую длину.
    /// </summary>
    InvalidSecondaryTagName = 8,

    /// <summary>
    ///     Ошибка доступа к файловому хранилищу при выполнении операции.
    /// </summary>
    StorageError = 9,
}
