using Shouldly;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.SharedKernel.Tests.Validation;

public sealed class FileSystemNameValidatorTests
{
    [Theory]
    [InlineData("мой тег")]
    [InlineData("45к артикул тест")]
    [InlineData("cover_1 (2).jpg")]
    [InlineData(".typoerp_id")]
    [InlineData("card_1.pdf")]
    [InlineData("console.txt")]
    [InlineData("com10.pdf")]
    [InlineData("lpt0.pdf")]
    [InlineData("aux_file.jpg")]
    public void Validate_WhenNameIsValidAcrossPlatforms_ShouldReturnSuccess(string name)
    {
        var error = FileSystemNameValidator.Validate(name);

        error.ShouldBe(FileSystemNameValidationError.None);
        FileSystemNameValidator.IsValid(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Validate_WhenNameIsNullOrWhitespace_ShouldReturnEmptyOrWhitespaceError(string? name)
    {
        var error = FileSystemNameValidator.Validate(name);

        error.ShouldBe(FileSystemNameValidationError.EmptyOrWhitespace);
        FileSystemNameValidator.IsValid(name).ShouldBeFalse();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    public void Validate_WhenNameConsistsOnlyOfDots_ShouldReturnOnlyDotsError(string name)
    {
        var error = FileSystemNameValidator.Validate(name);

        error.ShouldBe(FileSystemNameValidationError.OnlyDots);
        FileSystemNameValidator.IsValid(name).ShouldBeFalse();
    }

    [Theory]
    [InlineData("folder.")]
    [InlineData("file.jpg.")]
    [InlineData("folder ")]
    [InlineData("file.jpg ")]
    public void Validate_WhenNameEndsWithDotOrSpace_ShouldReturnEndsWithDotOrSpaceError(string name)
    {
        var error = FileSystemNameValidator.Validate(name);

        error.ShouldBe(FileSystemNameValidationError.EndsWithDotOrSpace);
        FileSystemNameValidator.IsValid(name).ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad<name")]
    [InlineData("bad>name")]
    [InlineData("bad:name")]
    [InlineData("bad\"name")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad|name")]
    [InlineData("bad?name")]
    [InlineData("bad*name")]
    [InlineData("bad\0name")]
    [InlineData("bad\tname")]
    [InlineData("bad\nname")]
    [InlineData("bad\u001Fname")]
    public void Validate_WhenNameContainsForbiddenOrControlChars_ShouldReturnForbiddenCharactersError(
        string name
    )
    {
        var error = FileSystemNameValidator.Validate(name);

        error.ShouldBe(FileSystemNameValidationError.ContainsForbiddenCharacters);
        FileSystemNameValidator.IsValid(name).ShouldBeFalse();
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("aux")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    [InlineData("con.txt")]
    [InlineData("AUX.card.pdf")]
    [InlineData("lpt1.tar.gz")]
    [InlineData("nul.jpg")]
    public void Validate_WhenNameIsWindowsReservedDeviceName_ShouldReturnReservedDeviceNameError(
        string name
    )
    {
        var error = FileSystemNameValidator.Validate(name);

        error.ShouldBe(FileSystemNameValidationError.ReservedDeviceName);
        FileSystemNameValidator.IsValid(name).ShouldBeFalse();
    }
}
