namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Режим массового изменения непервичных тегов у выбранных товаров.
/// </summary>
public enum BulkSecondaryTagOperationMode
{
    /// <summary>
    ///     Добавить указанные непервичные теги к уже имеющимся у товаров.
    /// </summary>
    Add = 0,

    /// <summary>
    ///     Удалить указанные непервичные теги у выбранных товаров.
    /// </summary>
    Remove = 1,

    /// <summary>
    ///     Полностью заменить список непервичных тегов у выбранных товаров на указанный набор.
    /// </summary>
    Replace = 2,
}
