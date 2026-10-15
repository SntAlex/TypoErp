namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Запрос на создание нового товара (или пустого черновика без файлов сборки).
/// </summary>
public sealed record CreateProductRequest(
    string PrimaryTagName,
    string Article,
    IReadOnlyList<ProductFileUpload>? Files = null,
    IReadOnlyList<string>? SecondaryTagNames = null
);
