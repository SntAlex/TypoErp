using Shouldly;
using TypoErp.Modules.Identity.Security;

namespace TypoErp.Modules.Identity.Tests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_AndVerifyPassword_WhenPasswordMatches_ShouldReturnTrue()
    {
        var hash = _hasher.HashPassword("StrongPass#2026");

        hash.ShouldNotBeNullOrWhiteSpace();
        _hasher.VerifyPassword("StrongPass#2026", hash).ShouldBeTrue();
        _hasher.VerifyPassword("WrongPass#2026", hash).ShouldBeFalse();
    }

    [Fact]
    public void HashPassword_WhenCalledTwiceForSamePassword_ShouldGenerateDifferentSalts()
    {
        var hash1 = _hasher.HashPassword("SamePassword123");
        var hash2 = _hasher.HashPassword("SamePassword123");

        hash1.ShouldNotBe(hash2);
        _hasher.VerifyPassword("SamePassword123", hash1).ShouldBeTrue();
        _hasher.VerifyPassword("SamePassword123", hash2).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-format")]
    [InlineData("PBKDF2$notanumber$AAAA$BBBB")]
    [InlineData("PBKDF2$100000$not-base64!$not-base64!")]
    public void VerifyPassword_WhenStoredHashIsMalformed_ShouldReturnFalse(string malformedHash)
    {
        _hasher.VerifyPassword("AnyPassword123", malformedHash).ShouldBeFalse();
    }
}
