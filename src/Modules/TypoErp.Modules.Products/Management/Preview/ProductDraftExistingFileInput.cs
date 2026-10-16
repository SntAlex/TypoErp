namespace TypoErp.Modules.Products.Management.Preview;

/// <summary>
///     Состояние существующего на диске файла товара в форме редактирования/починки для расчета превью.
/// </summary>
public sealed record ProductDraftExistingFileInput(
    string CurrentFileName,
    string NewFileName,
    long SizeBytes,
    string XxHash64,
    bool IsMarkedForDeletion = false
);
