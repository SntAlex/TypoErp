namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Статус операции переименования или перемещения папки/файла в хранилище товаров.
/// </summary>
public enum StorageRenameStatus
{
    /// <summary>
    ///     Успешно переименовано (включая смену регистра символов того же элемента).
    /// </summary>
    Success = 0,

    /// <summary>
    ///     Исходное и целевое имя полностью идентичны (переименование не требуется).
    /// </summary>
    NoChangeNeeded = 1,

    /// <summary>
    ///     Исходная папка или файл не найдены.
    /// </summary>
    SourceNotFound = 2,

    /// <summary>
    ///     В целевой директории уже существует другой объект с таким именем (без учета регистра).
    /// </summary>
    TargetCollision = 3,

    /// <summary>
    ///     Целевое имя невалидно для кроссплатформенной файловой системы.
    /// </summary>
    InvalidTargetName = 4,

    /// <summary>
    ///     Ошибка ввода-вывода или доступа к файловой системе.
    /// </summary>
    IoError = 5,
}
