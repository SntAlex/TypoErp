namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Данные файла для загрузки или diff-обновления в папке товара.
/// </summary>
public sealed record ProductFileUpload(string FileName, byte[] Content);
