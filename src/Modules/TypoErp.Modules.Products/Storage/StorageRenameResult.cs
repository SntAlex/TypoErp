namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Результат операции переименования или перемещения объекта в хранилище.
/// </summary>
public sealed record StorageRenameResult(
    StorageRenameStatus Status,
    string OriginalName,
    string FinalName,
    string? ErrorMessage = null
)
{
    public bool IsSuccess =>
        Status is StorageRenameStatus.Success or StorageRenameStatus.NoChangeNeeded;

    public bool IsConflict =>
        Status
            is StorageRenameStatus.TargetCollision
                or StorageRenameStatus.InvalidTargetName
                or StorageRenameStatus.IoError;
}
