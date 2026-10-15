namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Предоставляет человекочитаемые названия и подробные описания ошибок валидации товаров для отображения в UI.
/// </summary>
public static class ProductValidationErrorDescriptor
{
    public static string GetShortTitle(ProductValidationError error)
    {
        return error switch
        {
            ProductValidationError.MissingPrimaryTag => "Вне первичного тега",
            ProductValidationError.InvalidPrimaryTagName => "Некорректный первичный тег",
            ProductValidationError.PrimaryTagTooLong => "Слишком длинный первичный тег",
            ProductValidationError.PrimaryTagCapacityExceeded => "Превышена вместимость тега",
            ProductValidationError.InvalidArticleName => "Некорректное имя артикула",
            ProductValidationError.ArticleTooLong => "Слишком длинный артикул",
            ProductValidationError.ArticleMissingSpace => "Артикул без пробела (нет формата)",
            ProductValidationError.InvalidFileName => "Некорректное имя файла",
            ProductValidationError.InvalidCoverExtension => "Неверное расширение обложки",
            ProductValidationError.InvalidVideoExtension => "Неверное расширение видео",
            ProductValidationError.InvalidAssemblyFilePrefix => "Некорректный префикс сборки",
            ProductValidationError.MissingAssemblyFiles => "Нет файлов сборки (черновик)",
            ProductValidationError.InconsistentAssemblyPrefixes => "Разные префиксы файлов сборки",
            ProductValidationError.NormalizationConflict => "Конфликт авто-нормализации",
            ProductValidationError.InvalidProductIdFile => "Поврежден .typoerp_id",
            _ => error.ToString(),
        };
    }

    public static string GetDescription(ProductValidationError error)
    {
        return error switch
        {
            ProductValidationError.MissingPrimaryTag =>
                "Товар или файл находится непосредственно в корне хранилища вне папки первичного тега 1-го уровня.",
            ProductValidationError.InvalidPrimaryTagName =>
                "Название папки первичного тега содержит запрещенные символы или зарезервированные имена ОС (Linux/Windows/macOS).",
            ProductValidationError.PrimaryTagTooLong =>
                "Нормализованное название первичного тега превышает максимально допустимую длину.",
            ProductValidationError.PrimaryTagCapacityExceeded =>
                "Количество товаров внутри папки данного первичного тега превышает установленный лимит.",
            ProductValidationError.InvalidArticleName =>
                "Название папки артикула содержит запрещенные символы или недопустимо для кроссплатформенной файловой системы.",
            ProductValidationError.ArticleTooLong =>
                "Нормализованный артикул товара превышает максимально допустимую длину.",
            ProductValidationError.ArticleMissingSpace =>
                "Нормализованный артикул должен содержать хотя бы один пробел, чтобы выделить код формата до первого пробела.",
            ProductValidationError.InvalidFileName =>
                "Один или несколько файлов внутри папки товара имеют недопустимое для файловой системы имя.",
            ProductValidationError.InvalidCoverExtension =>
                "Файл обложки (префикс 'cover') имеет расширение, не входящее в список разрешенных форматов изображений.",
            ProductValidationError.InvalidVideoExtension =>
                "Файл видеообложки (префикс 'video') имеет расширение, не входящее в список разрешенных видеоформатов.",
            ProductValidationError.InvalidAssemblyFilePrefix =>
                "Не удалось выделить валидный префикс категории из имени файла сборки (например, имя начинается с '_' или пробела).",
            ProductValidationError.MissingAssemblyFiles =>
                "В папке товара отсутствуют файлы сборки (черновик). Загрузите хотя бы один файл сборки для определения категории.",
            ProductValidationError.InconsistentAssemblyPrefixes =>
                "Файлы сборки внутри папки товара имеют разные префиксы. Все файлы сборки одного товара обязаны иметь единый префикс категории.",
            ProductValidationError.NormalizationConflict =>
                "Не удалось автоматически переименовать папку или файл в нормализованный вид из-за коллизии с уже существующим именем на диске.",
            ProductValidationError.InvalidProductIdFile =>
                "Системный файл идентификатора товара (.typoerp_id) пуст или содержит некорректный GUID.",
            _ => $"Код ошибки валидации: {error}.",
        };
    }

    /// <summary>
    ///     Возвращает true, если единственной причиной невалидности товара является отсутствие файлов сборки (черновик).
    /// </summary>
    public static bool IsDraftOnly(IReadOnlyCollection<ProductValidationError>? errors)
    {
        return errors is { Count: 1 }
            && errors.Contains(ProductValidationError.MissingAssemblyFiles);
    }
}
