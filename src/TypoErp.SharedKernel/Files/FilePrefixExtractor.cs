using TypoErp.SharedKernel.Normalization;

namespace TypoErp.SharedKernel.Files;

/// <summary>
///     Выделяет префикс из имени файла согласно доменным правилам модуля товаров.
/// </summary>
public static class FilePrefixExtractor
{
    private static readonly char[] Delimiters = [' ', '_'];

    /// <summary>
    ///     Берет нормализованное имя файла без расширения и возвращает подстроку
    ///     до первого пробела ' ' или нижнего подчеркивания '_' (либо всё имя без расширения,
    ///     если разделителей нет) в нижнем регистре.
    /// </summary>
    public static string ExtractPrefix(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var normalizedFileName = StringNormalizer.NormalizeFileName(fileName);
        if (string.IsNullOrEmpty(normalizedFileName))
            return string.Empty;

        var stem = ExtractStemWithoutExtension(normalizedFileName);
        if (string.IsNullOrEmpty(stem))
            return string.Empty;

        var delimiterIndex = stem.IndexOfAny(Delimiters);
        return delimiterIndex >= 0 ? stem[..delimiterIndex] : stem;
    }

    private static string ExtractStemWithoutExtension(string normalizedFileName)
    {
        var lastDotIndex = normalizedFileName.LastIndexOf('.');
        if (lastDotIndex < 0)
            return normalizedFileName;

        if (lastDotIndex == 0)
            return string.Empty;

        return normalizedFileName[..lastDotIndex];
    }
}
