using System.Text;

namespace TypoErp.SharedKernel.Normalization;

/// <summary>
///     Единая нормализация строк и имен файлов согласно доменным правилам TypoErp.
/// </summary>
public static class StringNormalizer
{
    /// <summary>
    ///     Обрезает пробельные символы по краям, заменяет любые последовательности
    ///     пробельных символов внутри строки на один обычный пробел ' '
    ///     и переводит строку в нижний регистр (InvariantCulture).
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var span = value.AsSpan().Trim();
        var sb = new StringBuilder(span.Length);
        var previousWasWhitespace = false;

        foreach (var ch in span)
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                    previousWasWhitespace = true;
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                previousWasWhitespace = false;
            }

        return sb.ToString();
    }

    /// <summary>
    ///     Нормализует отдельно имя файла без расширения и его расширение
    ///     без использования платформозависимого парсинга разделителей пути.
    ///     Пример: "  Cover_1   (2).JPG " -> "cover_1 (2).jpg".
    /// </summary>
    public static string NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var trimmed = fileName.Trim();
        if (IsAllDots(trimmed))
            return trimmed;

        if (trimmed.EndsWith('.'))
            return Normalize(trimmed[..^1]) + ".";

        var lastDotIndex = trimmed.LastIndexOf('.');
        if (lastDotIndex < 0)
            return Normalize(trimmed);

        if (lastDotIndex == 0)
            return "." + Normalize(trimmed[1..]);

        var stem = Normalize(trimmed[..lastDotIndex]);
        var extension = Normalize(trimmed[lastDotIndex..]);

        return stem + extension;
    }

    private static bool IsAllDots(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        foreach (var ch in value)
            if (ch != '.')
                return false;

        return true;
    }
}
