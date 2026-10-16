namespace TypoErp.Modules.Products.Management.Preview;

/// <summary>
///     Статус изменения файла товара в форме создания/редактирования (с учетом diff-сверки по XxHash64).
/// </summary>
public enum ProductDraftFileDiffStatus
{
    /// <summary>
    ///     Существующий на диске файл без изменений.
    /// </summary>
    ExistingUnchanged = 0,

    /// <summary>
    ///     Существующий на диске файл будет переименован.
    /// </summary>
    RenamedOnDisk = 1,

    /// <summary>
    ///     Существующий на диске файл помечен на удаление.
    /// </summary>
    MarkedForDeletion = 2,

    /// <summary>
    ///     Загружаемый файл еще не существует в папке товара и будет записан как новый.
    /// </summary>
    NewFile = 3,

    /// <summary>
    ///     Загружаемый файл перезапишет существующий файл, так как его размер или XxHash64 отличаются.
    /// </summary>
    ContentModified = 4,

    /// <summary>
    ///     Загружаемый файл полностью идентичен существующему на диске по размеру и XxHash64 (перезапись не потребуется).
    /// </summary>
    UnchangedByHash = 5,
}
