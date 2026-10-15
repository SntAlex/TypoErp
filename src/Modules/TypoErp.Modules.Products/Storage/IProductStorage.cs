namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Абстракция файлового хранилища товаров (&lt;RootPath&gt; / &lt;Первичный Тег&gt; / &lt;Артикул&gt; / &lt;Файлы&gt;).
///     Является точкой правды для структуры папок, идентификаторов .typoerp_id и файлов товаров.
/// </summary>
public interface IProductStorage
{
    /// <summary>
    ///     Проверяет, существует ли и доступен ли корневой каталог хранилища.
    /// </summary>
    Task<bool> IsRootAccessibleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Возвращает список имен файлов, лежащих непосредственно в корне хранилища (вне первичных тегов).
    /// </summary>
    Task<IReadOnlyList<string>> GetRootFileNamesAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Возвращает список папок 1-го уровня (первичных тегов) с сохранением их текущего регистра на диске.
    /// </summary>
    Task<IReadOnlyList<string>> GetPrimaryTagDirectoriesAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Проверяет существование папки первичного тега (без учета регистра).
    /// </summary>
    Task<bool> PrimaryTagDirectoryExistsAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Создает папку первичного тега, если она еще не существует.
    /// </summary>
    Task CreatePrimaryTagDirectoryAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Безопасно переименовывает папку первичного тега (поддерживает смену только регистра и обнаруживает коллизии).
    /// </summary>
    Task<StorageRenameResult> RenamePrimaryTagDirectoryAsync(
        string currentName,
        string newName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Удаляет папку первичного тега.
    /// </summary>
    Task<bool> DeletePrimaryTagDirectoryAsync(
        string primaryTagName,
        bool recursive = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Возвращает список папок 2-го уровня (артикулов товаров) внутри указанного первичного тега.
    /// </summary>
    Task<IReadOnlyList<string>> GetProductDirectoriesAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Проверяет существование папки товара внутри первичного тега.
    /// </summary>
    Task<bool> ProductDirectoryExistsAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Создает папку товара (и при необходимости родительскую папку первичного тега).
    /// </summary>
    Task CreateProductDirectoryAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Безопасно переименовывает папку товара внутри того же первичного тега.
    /// </summary>
    Task<StorageRenameResult> RenameProductDirectoryAsync(
        string primaryTagName,
        string currentArticleName,
        string newArticleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Перемещает (и при необходимости переименовывает) папку товара между первичными тегами.
    /// </summary>
    Task<StorageRenameResult> MoveProductDirectoryAsync(
        string sourcePrimaryTagName,
        string sourceArticleName,
        string targetPrimaryTagName,
        string targetArticleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Удаляет папку товара со всем ее содержимым.
    /// </summary>
    Task<bool> DeleteProductDirectoryAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Читает и валидирует идентификатор товара из системного файла .typoerp_id.
    /// </summary>
    Task<ProductIdReadResult> ReadProductIdAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Записывает идентификатор товара в системный файл .typoerp_id внутри папки товара.
    /// </summary>
    Task WriteProductIdAsync(
        string primaryTagName,
        string articleName,
        Guid productId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Возвращает список всех файлов внутри папки товара с их размерами и хэшами XxHash64.
    /// </summary>
    Task<IReadOnlyList<StoredProductFileInfo>> GetProductFilesAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Возвращает метаданные конкретного файла внутри папки товара или null, если файл не найден.
    /// </summary>
    Task<StoredProductFileInfo?> GetProductFileInfoAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Открывает поток для чтения файла товара.
    /// </summary>
    Task<Stream> OpenReadProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Записывает или перезаписывает файл в папке товара, вычисляя его размер и XxHash64.
    /// </summary>
    Task<StoredProductFileInfo> WriteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Безопасно переименовывает файл внутри папки товара (с поддержкой смены регистра и проверкой коллизий).
    /// </summary>
    Task<StorageRenameResult> RenameProductFileAsync(
        string primaryTagName,
        string articleName,
        string currentFileName,
        string newFileName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Удаляет файл внутри папки товара.
    /// </summary>
    Task<bool> DeleteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    );
}
