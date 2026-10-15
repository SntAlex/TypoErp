namespace TypoErp.Modules.Identity.Management;

/// <summary>
///     Доменное исключение операций управления работниками, ролями и делегированием разрешений.
/// </summary>
public sealed class IdentityManagementException : InvalidOperationException
{
    public IdentityManagementException(IdentityManagementErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public IdentityManagementErrorCode ErrorCode { get; }
}
