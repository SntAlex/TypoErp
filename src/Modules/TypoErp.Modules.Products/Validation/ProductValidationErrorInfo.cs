namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Локализованное описание кода ошибки валидации товара для отображения в UI и диалогах починки.
/// </summary>
public sealed record ProductValidationErrorInfo(
    ProductValidationError Error,
    string ShortTitle,
    string Description,
    bool IsDraftState
)
{
    public static IReadOnlyList<ProductValidationError> AllErrors { get; } =
        Enum.GetValues<ProductValidationError>();

    public static ProductValidationErrorInfo Describe(ProductValidationError error)
    {
        if (error == ProductValidationError.MissingAssemblyFiles)
            return new ProductValidationErrorInfo(
                error,
                "Нет файлов сборки",
                "В папке товара отсутствуют файлы сборки (черновик товара). Загрузите хотя бы один файл сборки.",
                IsDraftState: true
            );

        if (error == ProductValidationError.ArticleMissingSpace)
            return new ProductValidationErrorInfo(
                error,
                "Артикул без пробела",
                "Нормализованный артикул обязан содержать хотя бы один пробел (до первого пробела выделяется код формата).",
                IsDraftState: false
            );

        if (error == ProductValidationError.PrimaryTagCapacityExceeded)
            return new ProductValidationErrorInfo(
                error,
                "Превышен лимит тега",
                "Превышено максимальное количество товаров внутри папки первичного тега.",
                IsDraftState: false
            );

        if (error == ProductValidationError.InvalidFileName)
            return new ProductValidationErrorInfo(
                error,
                "Некорректное имя файла",
                "Имя файла содержит запрещенные символы для Linux/Windows/macOS или зарезервированное имя ОС.",
                IsDraftState: false
            );

        if (error == ProductValidationError.InvalidCoverExtension)
            return new ProductValidationErrorInfo(
                error,
                "Неверное расширение обложки",
                "Файл с префиксом 'cover' имеет неразрешенное расширение изображения.",
                IsDraftState: false
            );

        if (error == ProductValidationError.InvalidVideoExtension)
            return new ProductValidationErrorInfo(
                error,
                "Неверное расширение видео",
                "Файл с префиксом 'video' имеет неразрешенное расширение видеофайла.",
                IsDraftState: false
            );

        if (error == ProductValidationError.InvalidAssemblyFilePrefix)
            return new ProductValidationErrorInfo(
                error,
                "Некорректный префикс сборки",
                "Не удалось выделить валидный префикс категории из имени файла сборки.",
                IsDraftState: false
            );

        if (error == ProductValidationError.NormalizationConflict)
            return new ProductValidationErrorInfo(
                error,
                "Конфликт нормализации",
                "Автоматическое переименование невозможно из-за коллизии имен на диске или недопустимых символов.",
                IsDraftState: false
            );

        if (error == ProductValidationError.InvalidProductIdFile)
            return new ProductValidationErrorInfo(
                error,
                "Поврежден .typoerp_id",
                "Системный файл идентификатора .typoerp_id содержит некорректный Guid.",
                IsDraftState: false
            );

        return error.ToString() switch
        {
            "MissingPrimaryTag" or "OutsidePrimaryTag" or "ProductOutsidePrimaryTag" =>
                new ProductValidationErrorInfo(
                    error,
                    "Вне первичного тега",
                    "Объект расположен непосредственно в корне хранилища вне папки первичного тега.",
                    IsDraftState: false
                ),
            "EmptyPrimaryTag" or "PrimaryTagEmpty" or "InvalidPrimaryTagName" =>
                new ProductValidationErrorInfo(
                    error,
                    "Некорректный первичный тег",
                    "Имя папки первичного тега пустое или содержит запрещенные для ФС символы.",
                    IsDraftState: false
                ),
            "PrimaryTagTooLong" or "PrimaryTagMaxLengthExceeded" => new ProductValidationErrorInfo(
                error,
                "Слишком длинный первичный тег",
                "Нормализованное имя первичного тега превышает максимально допустимую длину.",
                IsDraftState: false
            ),
            "EmptyArticle" or "ArticleEmpty" or "InvalidArticleName" =>
                new ProductValidationErrorInfo(
                    error,
                    "Некорректный артикул",
                    "Имя папки артикула пустое или содержит запрещенные для кроссплатформенной ФС символы.",
                    IsDraftState: false
                ),
            "ArticleTooLong" or "ArticleMaxLengthExceeded" => new ProductValidationErrorInfo(
                error,
                "Слишком длинный артикул",
                "Нормализованный артикул превышает максимально допустимую длину (60 символов).",
                IsDraftState: false
            ),
            _ => new ProductValidationErrorInfo(
                error,
                "Конфликт префиксов сборки",
                "Все файлы сборки внутри папки одного товара обязаны иметь единый префикс категории.",
                IsDraftState: false
            ),
        };
    }
}
