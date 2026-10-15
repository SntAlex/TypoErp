namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Результат чтения системного файла .typoerp_id внутри папки товара.
/// </summary>
public sealed record ProductIdReadResult(
    ProductIdReadStatus Status,
    Guid? ProductId,
    string? RawContent = null
);
