namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Запрос на редактирование или починку существующего товара (с поддержкой переименования артикула/тега,
///     переименования проблемных файлов, удаления файлов и diff-загрузки по XxHash64).
/// </summary>
public sealed record UpdateProductRequest(
    Guid ProductId,
    string NewPrimaryTagName,
    string NewArticle,
    IReadOnlyList<ProductFileUpload>? FilesToUpsert = null,
    IReadOnlyList<string>? FilesToDelete = null,
    IReadOnlyList<ProductFileRenameInput>? FileRenames = null,
    IReadOnlyList<string>? SecondaryTagNames = null
);
