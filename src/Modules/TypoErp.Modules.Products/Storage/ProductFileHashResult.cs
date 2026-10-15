namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Результат вычисления 64-битного хэша XxHash64 и размера файла в байтах.
/// </summary>
public readonly record struct ProductFileHashResult(ulong Hash, string HashHex, long SizeBytes);
