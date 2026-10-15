namespace TypoErp.Modules.Products.Sync;

/// <summary>
///     Итоговая статистика выполнения синхронизации файлового хранилища товаров с PostgreSQL.
/// </summary>
public sealed record ProductSyncResult(
    DateTimeOffset SynchronizedAtUtc,
    int TotalProducts,
    int ValidProducts,
    int InvalidProducts,
    int AddedProducts,
    int UpdatedProducts,
    int DeletedProducts,
    int AutoNormalizedItemsCount,
    int NormalizationConflictsCount,
    int AddedOrUpdatedFilesCount,
    int UnchangedFilesCount,
    int DeletedFilesCount
);
