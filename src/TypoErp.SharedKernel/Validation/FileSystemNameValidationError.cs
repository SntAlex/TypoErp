namespace TypoErp.SharedKernel.Validation;

/// <summary>
///     Коды ошибок кроссплатформенной валидации имен файлов и папок (Linux, Windows, macOS).
/// </summary>
public enum FileSystemNameValidationError
{
    None = 0,
    EmptyOrWhitespace = 1,
    OnlyDots = 2,
    EndsWithDotOrSpace = 3,
    ContainsForbiddenCharacters = 4,
    ReservedDeviceName = 5,
}
