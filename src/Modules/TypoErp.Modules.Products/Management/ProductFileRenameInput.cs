namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Запрос на переименование существующего файла внутри папки товара на диске (например, при починке конфликтов).
/// </summary>
public sealed record ProductFileRenameInput(string CurrentFileName, string NewFileName);
