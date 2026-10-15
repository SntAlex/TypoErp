namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Статус чтения системного файла идентификатора товара (.typoerp_id).
/// </summary>
public enum ProductIdReadStatus
{
    /// <summary>
    ///     Файл существует и содержит валидный непустой Guid.
    /// </summary>
    Found = 0,

    /// <summary>
    ///     Файл .typoerp_id отсутствует в папке товара.
    /// </summary>
    NotFound = 1,

    /// <summary>
    ///     Файл существует, но пуст или содержит некорректный Guid.
    /// </summary>
    InvalidContent = 2,
}
