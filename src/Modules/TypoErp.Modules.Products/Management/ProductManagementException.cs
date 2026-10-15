namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Доменное исключение операций создания, редактирования, починки и массового управления товарами.
/// </summary>
public sealed class ProductManagementException : InvalidOperationException
{
    public ProductManagementException(ProductManagementErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public ProductManagementErrorCode ErrorCode { get; }
}
