namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Метаданные физического файла внутри папки товара в хранилище.
/// </summary>
public sealed record StoredProductFileInfo(string FileName, long SizeBytes, string XxHash64);
