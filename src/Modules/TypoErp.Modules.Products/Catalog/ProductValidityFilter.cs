namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Режим фильтрации товаров по статусу валидности.
/// </summary>
public enum ProductValidityFilter
{
    /// <summary>
    ///     Все товары (валидные, черновики и товары с ошибками).
    /// </summary>
    All = 0,

    /// <summary>
    ///     Только полностью валидные товары (IsValid = true).
    /// </summary>
    ValidOnly = 1,

    /// <summary>
    ///     Только невалидные товары и черновики (IsValid = false).
    /// </summary>
    InvalidOnly = 2,
}
