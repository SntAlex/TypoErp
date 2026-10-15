using System.Buffers;

namespace TypoErp.SharedKernel.Validation;

/// <summary>
///     Кроссплатформенный валидатор имен папок и файлов для Linux, Windows и macOS.
/// </summary>
public static class FileSystemNameValidator
{
    private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create(
        "<>:\"\\/|?*\0\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F"
    );

    private static readonly HashSet<string> ReservedWindowsNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    };

    /// <summary>
    ///     Возвращает true, если имя файла или папки валидно во всех поддерживаемых ОС (Linux, Windows, macOS).
    /// </summary>
    public static bool IsValid(string? name)
    {
        return Validate(name) == FileSystemNameValidationError.None;
    }

    /// <summary>
    ///     Проверяет имя файла или папки и возвращает конкретную причину невалидности или
    ///     <see cref="FileSystemNameValidationError.None" />.
    /// </summary>
    public static FileSystemNameValidationError Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FileSystemNameValidationError.EmptyOrWhitespace;

        var span = name.AsSpan();

        if (span.IndexOfAny(ForbiddenCharacters) >= 0)
            return FileSystemNameValidationError.ContainsForbiddenCharacters;

        if (IsAllDots(span))
            return FileSystemNameValidationError.OnlyDots;

        var lastChar = span[^1];
        if (lastChar is '.' or ' ')
            return FileSystemNameValidationError.EndsWithDotOrSpace;

        var dotIndex = span.IndexOf('.');
        var stemBeforeFirstDot = (dotIndex >= 0 ? span[..dotIndex] : span).TrimEnd(' ');

        if (
            !stemBeforeFirstDot.IsEmpty
            && ReservedWindowsNames.Contains(stemBeforeFirstDot.ToString())
        )
            return FileSystemNameValidationError.ReservedDeviceName;

        return FileSystemNameValidationError.None;
    }

    private static bool IsAllDots(ReadOnlySpan<char> span)
    {
        foreach (var ch in span)
            if (ch != '.')
                return false;

        return true;
    }
}
