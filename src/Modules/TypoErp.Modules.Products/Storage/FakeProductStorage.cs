using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TypoErp.Modules.Products.Configuration;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Потокобезопасная In-Memory реализация <see cref="IProductStorage" /> для разработки и тестирования.
///     Сохраняет точный регистр имен, поддерживает переименование со сменой только регистра
///     и детектирует регистронезависимые коллизии между разными объектами.
/// </summary>
public sealed class FakeProductStorage : IProductStorage
{
    private readonly ILogger<FakeProductStorage> _logger;
    private readonly ProductsStorageOptions _options;
    private readonly List<PrimaryTagNode> _primaryTags = [];
    private readonly List<FileNode> _rootFiles = [];
    private readonly Lock _syncRoot = new();

    public FakeProductStorage(
        ProductsStorageOptions? options = null,
        ILogger<FakeProductStorage>? logger = null
    )
    {
        _options = options ?? new ProductsStorageOptions();
        _logger = logger ?? NullLogger<FakeProductStorage>.Instance;
    }

    /// <summary>
    ///     Позволяет в тестах симулировать недоступность корневой директории хранилища.
    /// </summary>
    public bool IsRootAccessible { get; set; } = true;

    public Task<bool> IsRootAccessibleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult(IsRootAccessible);
        }
    }

    public Task<IReadOnlyList<string>> GetRootFileNamesAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var names = _rootFiles
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    public Task<IReadOnlyList<string>> GetPrimaryTagDirectoriesAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var names = _primaryTags
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    public Task<bool> PrimaryTagDirectoryExistsAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            return Task.FromResult(FindPrimaryTag(primaryTagName) is not null);
        }
    }

    public Task CreatePrimaryTagDirectoryAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryTagName);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            EnsureRootAccessible();
            if (FindPrimaryTag(primaryTagName) is null)
            {
                _primaryTags.Add(new PrimaryTagNode(primaryTagName));
                _logger.LogInformation(
                    "Created primary tag directory '{PrimaryTag}' in FakeProductStorage",
                    primaryTagName
                );
            }
        }

        return Task.CompletedTask;
    }

    public Task<StorageRenameResult> RenamePrimaryTagDirectoryAsync(
        string currentName,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var source = FindPrimaryTag(currentName);
            if (source is null)
            {
                _logger.LogWarning(
                    "Cannot rename primary tag '{CurrentName}' to '{NewName}': source not found",
                    currentName,
                    newName
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.SourceNotFound,
                        currentName,
                        currentName,
                        $"Primary tag directory '{currentName}' was not found."
                    )
                );
            }

            if (string.Equals(source.Name, newName, StringComparison.Ordinal))
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.NoChangeNeeded,
                        source.Name,
                        source.Name
                    )
                );

            if (!FileSystemNameValidator.IsValid(newName))
            {
                _logger.LogWarning(
                    "Cannot rename primary tag '{CurrentName}' to invalid name '{NewName}'",
                    source.Name,
                    newName
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.InvalidTargetName,
                        source.Name,
                        source.Name,
                        $"Target primary tag name '{newName}' is invalid for file system."
                    )
                );
            }

            var conflicting = _primaryTags.FirstOrDefault(t =>
                !ReferenceEquals(t, source)
                && string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase)
            );

            if (conflicting is not null)
            {
                _logger.LogWarning(
                    "Collision while renaming primary tag '{CurrentName}' to '{NewName}': target '{ExistingName}' already exists",
                    source.Name,
                    newName,
                    conflicting.Name
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.TargetCollision,
                        source.Name,
                        source.Name,
                        $"Primary tag directory '{conflicting.Name}' already exists."
                    )
                );
            }

            var previousName = source.Name;
            source.Name = newName;
            _logger.LogInformation(
                "Renamed primary tag directory '{OldName}' -> '{NewName}'",
                previousName,
                newName
            );

            return Task.FromResult(
                new StorageRenameResult(StorageRenameStatus.Success, previousName, newName)
            );
        }
    }

    public Task<bool> DeletePrimaryTagDirectoryAsync(
        string primaryTagName,
        bool recursive = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            if (tag is null)
                return Task.FromResult(false);

            if (!recursive && tag.Products.Count > 0)
                throw new IOException(
                    $"Primary tag directory '{tag.Name}' is not empty ({tag.Products.Count} products inside)."
                );

            _primaryTags.Remove(tag);
            _logger.LogInformation("Deleted primary tag directory '{PrimaryTag}'", tag.Name);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<string>> GetProductDirectoriesAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            if (tag is null)
                return Task.FromResult<IReadOnlyList<string>>([]);

            var names = tag
                .Products.Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<string>>(names);
        }
    }

    public Task<bool> ProductDirectoryExistsAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            return Task.FromResult(tag is not null && FindProduct(tag, articleName) is not null);
        }
    }

    public Task CreateProductDirectoryAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryTagName);
        ArgumentException.ThrowIfNullOrWhiteSpace(articleName);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            if (tag is null)
            {
                tag = new PrimaryTagNode(primaryTagName);
                _primaryTags.Add(tag);
            }

            if (FindProduct(tag, articleName) is null)
            {
                tag.Products.Add(new ProductNode(articleName));
                _logger.LogInformation(
                    "Created product directory '{PrimaryTag}/{Article}' in FakeProductStorage",
                    tag.Name,
                    articleName
                );
            }
        }

        return Task.CompletedTask;
    }

    public Task<StorageRenameResult> RenameProductDirectoryAsync(
        string primaryTagName,
        string currentArticleName,
        string newArticleName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            var product = tag is null ? null : FindProduct(tag, currentArticleName);
            if (tag is null || product is null)
            {
                _logger.LogWarning(
                    "Cannot rename product '{PrimaryTag}/{CurrentArticle}' to '{NewArticle}': source not found",
                    primaryTagName,
                    currentArticleName,
                    newArticleName
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.SourceNotFound,
                        currentArticleName,
                        currentArticleName,
                        $"Product directory '{primaryTagName}/{currentArticleName}' was not found."
                    )
                );
            }

            if (string.Equals(product.Name, newArticleName, StringComparison.Ordinal))
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.NoChangeNeeded,
                        product.Name,
                        product.Name
                    )
                );

            if (!FileSystemNameValidator.IsValid(newArticleName))
            {
                _logger.LogWarning(
                    "Cannot rename product '{PrimaryTag}/{CurrentArticle}' to invalid name '{NewArticle}'",
                    tag.Name,
                    product.Name,
                    newArticleName
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.InvalidTargetName,
                        product.Name,
                        product.Name,
                        $"Target article name '{newArticleName}' is invalid for file system."
                    )
                );
            }

            var conflicting = tag.Products.FirstOrDefault(p =>
                !ReferenceEquals(p, product)
                && string.Equals(p.Name, newArticleName, StringComparison.OrdinalIgnoreCase)
            );

            if (conflicting is not null)
            {
                _logger.LogWarning(
                    "Collision while renaming product '{PrimaryTag}/{CurrentArticle}' to '{NewArticle}': '{ExistingArticle}' already exists",
                    tag.Name,
                    product.Name,
                    newArticleName,
                    conflicting.Name
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.TargetCollision,
                        product.Name,
                        product.Name,
                        $"Product directory '{conflicting.Name}' already exists in '{tag.Name}'."
                    )
                );
            }

            var previousName = product.Name;
            product.Name = newArticleName;
            _logger.LogInformation(
                "Renamed product directory '{PrimaryTag}/{OldArticle}' -> '{NewArticle}'",
                tag.Name,
                previousName,
                newArticleName
            );

            return Task.FromResult(
                new StorageRenameResult(StorageRenameStatus.Success, previousName, newArticleName)
            );
        }
    }

    public Task<StorageRenameResult> MoveProductDirectoryAsync(
        string sourcePrimaryTagName,
        string sourceArticleName,
        string targetPrimaryTagName,
        string targetArticleName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var sourceTag = FindPrimaryTag(sourcePrimaryTagName);
            var product = sourceTag is null ? null : FindProduct(sourceTag, sourceArticleName);
            if (sourceTag is null || product is null)
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.SourceNotFound,
                        sourceArticleName,
                        sourceArticleName,
                        $"Source product '{sourcePrimaryTagName}/{sourceArticleName}' was not found."
                    )
                );

            if (
                !FileSystemNameValidator.IsValid(targetPrimaryTagName)
                || !FileSystemNameValidator.IsValid(targetArticleName)
            )
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.InvalidTargetName,
                        product.Name,
                        product.Name,
                        "Target primary tag or article name is invalid for file system."
                    )
                );

            var targetTag = FindPrimaryTag(targetPrimaryTagName);
            if (ReferenceEquals(sourceTag, targetTag))
                return RenameProductDirectoryAsync(
                    sourcePrimaryTagName,
                    sourceArticleName,
                    targetArticleName,
                    cancellationToken
                );

            if (targetTag is null)
            {
                targetTag = new PrimaryTagNode(targetPrimaryTagName);
                _primaryTags.Add(targetTag);
            }

            var conflicting = targetTag.Products.FirstOrDefault(p =>
                string.Equals(p.Name, targetArticleName, StringComparison.OrdinalIgnoreCase)
            );

            if (conflicting is not null)
            {
                _logger.LogWarning(
                    "Collision while moving product '{SourceTag}/{SourceArticle}' to '{TargetTag}/{TargetArticle}'",
                    sourceTag.Name,
                    product.Name,
                    targetTag.Name,
                    targetArticleName
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.TargetCollision,
                        product.Name,
                        product.Name,
                        $"Product '{conflicting.Name}' already exists in '{targetTag.Name}'."
                    )
                );
            }

            var previousArticle = product.Name;
            sourceTag.Products.Remove(product);
            product.Name = targetArticleName;
            targetTag.Products.Add(product);

            _logger.LogInformation(
                "Moved product directory '{SourceTag}/{SourceArticle}' -> '{TargetTag}/{TargetArticle}'",
                sourceTag.Name,
                previousArticle,
                targetTag.Name,
                targetArticleName
            );

            return Task.FromResult(
                new StorageRenameResult(
                    StorageRenameStatus.Success,
                    previousArticle,
                    targetArticleName
                )
            );
        }
    }

    public Task<bool> DeleteProductDirectoryAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            var product = tag is null ? null : FindProduct(tag, articleName);
            if (tag is null || product is null)
                return Task.FromResult(false);

            tag.Products.Remove(product);
            _logger.LogInformation(
                "Deleted product directory '{PrimaryTag}/{Article}'",
                tag.Name,
                product.Name
            );
            return Task.FromResult(true);
        }
    }

    public Task<ProductIdReadResult> ReadProductIdAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var product = GetRequiredProduct(primaryTagName, articleName);
            var idFileName = GetProductIdFileName();
            var file = FindFile(product, idFileName);

            if (file is null)
                return Task.FromResult(new ProductIdReadResult(ProductIdReadStatus.NotFound, null));

            var rawText = Encoding.UTF8.GetString(file.Content).Trim();
            if (Guid.TryParse(rawText, out var parsedId) && parsedId != Guid.Empty)
                return Task.FromResult(
                    new ProductIdReadResult(ProductIdReadStatus.Found, parsedId, rawText)
                );

            _logger.LogWarning(
                "Invalid .typoerp_id content in '{PrimaryTag}/{Article}': '{RawContent}'",
                primaryTagName,
                articleName,
                rawText
            );
            return Task.FromResult(
                new ProductIdReadResult(ProductIdReadStatus.InvalidContent, null, rawText)
            );
        }
    }

    public Task WriteProductIdAsync(
        string primaryTagName,
        string articleName,
        Guid productId,
        CancellationToken cancellationToken = default
    )
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId cannot be Guid.Empty.", nameof(productId));

        var bytes = Encoding.UTF8.GetBytes(productId.ToString("D"));
        return WriteProductFileAsync(
            primaryTagName,
            articleName,
            GetProductIdFileName(),
            bytes,
            cancellationToken
        );
    }

    public Task<IReadOnlyList<StoredProductFileInfo>> GetProductFilesAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var product = GetRequiredProduct(primaryTagName, articleName);
            var files = product
                .Files.OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new StoredProductFileInfo(f.Name, f.SizeBytes, f.XxHash64))
                .ToList();

            return Task.FromResult<IReadOnlyList<StoredProductFileInfo>>(files);
        }
    }

    public Task<StoredProductFileInfo?> GetProductFileInfoAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            var product = tag is null ? null : FindProduct(tag, articleName);
            var file = product is null ? null : FindFile(product, fileName);

            return Task.FromResult(
                file is null
                    ? null
                    : new StoredProductFileInfo(file.Name, file.SizeBytes, file.XxHash64)
            );
        }
    }

    public Task<Stream> OpenReadProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var product = GetRequiredProduct(primaryTagName, articleName);
            var file = FindFile(product, fileName);
            if (file is null)
                throw new FileNotFoundException(
                    $"File '{fileName}' was not found in '{primaryTagName}/{articleName}'.",
                    fileName
                );

            var copy = file.Content.ToArray();
            return Task.FromResult<Stream>(new MemoryStream(copy, writable: false));
        }
    }

    public async Task<StoredProductFileInfo> WriteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        return await WriteProductFileAsync(
            primaryTagName,
            articleName,
            fileName,
            ms.ToArray(),
            cancellationToken
        );
    }

    public Task<StoredProductFileInfo> WriteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var copy = content.ToArray();
        var hashResult = ProductFileHasher.Compute(copy);

        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var product = GetRequiredProduct(primaryTagName, articleName);
            var existing = FindFile(product, fileName);

            if (existing is not null)
            {
                existing.Content = copy;
                existing.SizeBytes = hashResult.SizeBytes;
                existing.XxHash64 = hashResult.HashHex;
            }
            else
            {
                existing = new FileNode(fileName, copy, hashResult.SizeBytes, hashResult.HashHex);
                product.Files.Add(existing);
            }

            _logger.LogInformation(
                "Wrote file '{PrimaryTag}/{Article}/{FileName}' ({SizeBytes} bytes, XxHash64={Hash})",
                primaryTagName,
                articleName,
                existing.Name,
                existing.SizeBytes,
                existing.XxHash64
            );

            return Task.FromResult(
                new StoredProductFileInfo(existing.Name, existing.SizeBytes, existing.XxHash64)
            );
        }
    }

    public Task<StorageRenameResult> RenameProductFileAsync(
        string primaryTagName,
        string articleName,
        string currentFileName,
        string newFileName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            var product = tag is null ? null : FindProduct(tag, articleName);
            var file = product is null ? null : FindFile(product, currentFileName);

            if (tag is null || product is null || file is null)
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.SourceNotFound,
                        currentFileName,
                        currentFileName,
                        $"File '{currentFileName}' was not found in '{primaryTagName}/{articleName}'."
                    )
                );

            if (string.Equals(file.Name, newFileName, StringComparison.Ordinal))
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.NoChangeNeeded,
                        file.Name,
                        file.Name
                    )
                );

            if (!FileSystemNameValidator.IsValid(newFileName))
            {
                _logger.LogWarning(
                    "Cannot rename file '{PrimaryTag}/{Article}/{CurrentFile}' to invalid name '{NewFile}'",
                    tag.Name,
                    product.Name,
                    file.Name,
                    newFileName
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.InvalidTargetName,
                        file.Name,
                        file.Name,
                        $"Target file name '{newFileName}' is invalid for file system."
                    )
                );
            }

            var conflicting = product.Files.FirstOrDefault(f =>
                !ReferenceEquals(f, file)
                && string.Equals(f.Name, newFileName, StringComparison.OrdinalIgnoreCase)
            );

            if (conflicting is not null)
            {
                _logger.LogWarning(
                    "Collision while renaming file '{PrimaryTag}/{Article}/{CurrentFile}' to '{NewFile}': '{ExistingFile}' already exists",
                    tag.Name,
                    product.Name,
                    file.Name,
                    newFileName,
                    conflicting.Name
                );
                return Task.FromResult(
                    new StorageRenameResult(
                        StorageRenameStatus.TargetCollision,
                        file.Name,
                        file.Name,
                        $"File '{conflicting.Name}' already exists in '{tag.Name}/{product.Name}'."
                    )
                );
            }

            var previousName = file.Name;
            file.Name = newFileName;
            _logger.LogInformation(
                "Renamed file '{PrimaryTag}/{Article}/{OldFile}' -> '{NewFile}'",
                tag.Name,
                product.Name,
                previousName,
                newFileName
            );

            return Task.FromResult(
                new StorageRenameResult(StorageRenameStatus.Success, previousName, newFileName)
            );
        }
    }

    public Task<bool> DeleteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            EnsureRootAccessible();
            var tag = FindPrimaryTag(primaryTagName);
            var product = tag is null ? null : FindProduct(tag, articleName);
            var file = product is null ? null : FindFile(product, fileName);

            if (product is null || file is null)
                return Task.FromResult(false);

            product.Files.Remove(file);
            _logger.LogInformation(
                "Deleted file '{PrimaryTag}/{Article}/{FileName}'",
                primaryTagName,
                articleName,
                file.Name
            );
            return Task.FromResult(true);
        }
    }

    /// <summary>
    ///     Вспомогательный метод для тестов: добавляет файл непосредственно в корень хранилища (вне первичного тега).
    /// </summary>
    public void SeedRootFile(string fileName, byte[]? content = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var bytes = content?.ToArray() ?? [];
        var hash = ProductFileHasher.Compute(bytes);

        lock (_syncRoot)
        {
            _rootFiles.RemoveAll(f =>
                string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)
            );
            _rootFiles.Add(new FileNode(fileName, bytes, hash.SizeBytes, hash.HashHex));
        }
    }

    /// <summary>
    ///     Вспомогательный метод для тестов: удаляет файл из корня хранилища.
    /// </summary>
    public bool RemoveRootFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        lock (_syncRoot)
        {
            return _rootFiles.RemoveAll(f =>
                    string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)
                ) > 0;
        }
    }

    private void EnsureRootAccessible()
    {
        if (!IsRootAccessible)
        {
            _logger.LogError("Products storage root is not accessible");
            throw new InvalidOperationException(
                "Корневой путь хранилища товаров не существует или недоступен."
            );
        }
    }

    private string GetProductIdFileName()
    {
        return string.IsNullOrWhiteSpace(_options.ProductIdFileName)
            ? ProductsStorageOptions.DefaultProductIdFileName
            : _options.ProductIdFileName.Trim();
    }

    private ProductNode GetRequiredProduct(string primaryTagName, string articleName)
    {
        var tag = FindPrimaryTag(primaryTagName);
        var product = tag is null ? null : FindProduct(tag, articleName);
        if (tag is null || product is null)
            throw new DirectoryNotFoundException(
                $"Product directory '{primaryTagName}/{articleName}' does not exist."
            );

        return product;
    }

    private PrimaryTagNode? FindPrimaryTagExact(string name)
    {
        return _primaryTags.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.Ordinal)
        );
    }

    private PrimaryTagNode? FindPrimaryTag(string name)
    {
        return FindPrimaryTagExact(name)
            ?? _primaryTags.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static ProductNode? FindProductExact(PrimaryTagNode tag, string articleName)
    {
        return tag.Products.FirstOrDefault(p =>
            string.Equals(p.Name, articleName, StringComparison.Ordinal)
        );
    }

    private static ProductNode? FindProduct(PrimaryTagNode tag, string articleName)
    {
        return FindProductExact(tag, articleName)
            ?? tag.Products.FirstOrDefault(p =>
                string.Equals(p.Name, articleName, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static FileNode? FindFileExact(ProductNode product, string fileName)
    {
        return product.Files.FirstOrDefault(f =>
            string.Equals(f.Name, fileName, StringComparison.Ordinal)
        );
    }

    private static FileNode? FindFile(ProductNode product, string fileName)
    {
        return FindFileExact(product, fileName)
            ?? product.Files.FirstOrDefault(f =>
                string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)
            );
    }

    private sealed class PrimaryTagNode(string name)
    {
        public string Name { get; set; } = name;
        public List<ProductNode> Products { get; } = [];
    }

    private sealed class ProductNode(string name)
    {
        public string Name { get; set; } = name;
        public List<FileNode> Files { get; } = [];
    }

    private sealed class FileNode(string name, byte[] content, long sizeBytes, string xxHash64)
    {
        public string Name { get; set; } = name;
        public byte[] Content { get; set; } = content;
        public long SizeBytes { get; set; } = sizeBytes;
        public string XxHash64 { get; set; } = xxHash64;
    }
}
