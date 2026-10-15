using System.Buffers;
using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TypoErp.Modules.Products.Configuration;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Кроссплатформенная реализация <see cref="IProductStorage" /> для работы с реальной файловой системой
///     (Linux, Windows, macOS). Поддерживает двухшаговое переименование при смене только регистра символов,
///     потоковое вычисление XxHash64 и защиту от регистронезависимых коллизий.
/// </summary>
public sealed class LocalDiskProductStorage : IProductStorage
{
    private const int StreamBufferSize = 64 * 1024;
    private const string TempRenamePrefix = ".typoerp_tmp_";

    private readonly ILogger<LocalDiskProductStorage> _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly ProductsStorageOptions _options;

    public LocalDiskProductStorage(
        ProductsStorageOptions options,
        ILogger<LocalDiskProductStorage>? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger ?? NullLogger<LocalDiskProductStorage>.Instance;
    }

    public async Task<bool> IsRootAccessibleAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            return IsRootAccessibleInternal();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetRootFileNamesAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var names = rootDir
                .EnumerateFiles()
                .Where(f => !IsTempName(f.Name))
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            return names;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetPrimaryTagDirectoriesAsync(
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var names = rootDir
                .EnumerateDirectories()
                .Where(d => !IsTempName(d.Name))
                .Select(d => d.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            return names;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> PrimaryTagDirectoryExistsAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            return FindSubDirectory(rootDir, primaryTagName) is not null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task CreatePrimaryTagDirectoryAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryTagName);
        EnsureSinglePathSegment(primaryTagName, nameof(primaryTagName));

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            if (FindSubDirectory(rootDir, primaryTagName) is null)
            {
                var targetPath = GetSafeChildPath(rootDir.FullName, primaryTagName);
                Directory.CreateDirectory(targetPath);
                _logger.LogInformation(
                    "Created primary tag directory '{PrimaryTag}' on disk at '{Path}'",
                    primaryTagName,
                    targetPath
                );
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<StorageRenameResult> RenamePrimaryTagDirectoryAsync(
        string currentName,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var sourceDir = FindSubDirectory(rootDir, currentName);
            if (sourceDir is null)
            {
                _logger.LogWarning(
                    "Cannot rename primary tag '{CurrentName}' to '{NewName}': source directory not found",
                    currentName,
                    newName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.SourceNotFound,
                    currentName,
                    currentName,
                    $"Primary tag directory '{currentName}' was not found."
                );
            }

            if (string.Equals(sourceDir.Name, newName, StringComparison.Ordinal))
                return new StorageRenameResult(
                    StorageRenameStatus.NoChangeNeeded,
                    sourceDir.Name,
                    sourceDir.Name
                );

            if (!FileSystemNameValidator.IsValid(newName))
            {
                _logger.LogWarning(
                    "Cannot rename primary tag '{CurrentName}' to invalid name '{NewName}'",
                    sourceDir.Name,
                    newName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.InvalidTargetName,
                    sourceDir.Name,
                    sourceDir.Name,
                    $"Target primary tag name '{newName}' is invalid for file system."
                );
            }

            var conflictingEntryName = FindConflictingEntryName(rootDir, sourceDir.Name, newName);
            if (conflictingEntryName is not null)
            {
                _logger.LogWarning(
                    "Collision while renaming primary tag '{CurrentName}' to '{NewName}': '{ExistingName}' already exists",
                    sourceDir.Name,
                    newName,
                    conflictingEntryName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.TargetCollision,
                    sourceDir.Name,
                    sourceDir.Name,
                    $"Primary tag directory '{conflictingEntryName}' already exists."
                );
            }

            var previousName = sourceDir.Name;
            var renameResult = RenameDirectorySafe(rootDir, sourceDir, newName);
            if (renameResult.IsSuccess)
            {
                _logger.LogInformation(
                    "Renamed primary tag directory on disk '{OldName}' -> '{NewName}'",
                    previousName,
                    newName
                );
            }

            return renameResult;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> DeletePrimaryTagDirectoryAsync(
        string primaryTagName,
        bool recursive = false,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            if (tagDir is null)
                return false;

            if (!recursive && tagDir.EnumerateFileSystemInfos().Any(e => !IsTempName(e.Name)))
                throw new IOException($"Primary tag directory '{tagDir.Name}' is not empty.");

            var actualName = tagDir.Name;
            tagDir.Delete(recursive: true);
            _logger.LogInformation(
                "Deleted primary tag directory '{PrimaryTag}' from disk",
                actualName
            );
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetProductDirectoriesAsync(
        string primaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            if (tagDir is null)
                return [];

            return tagDir
                .EnumerateDirectories()
                .Where(d => !IsTempName(d.Name))
                .Select(d => d.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> ProductDirectoryExistsAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            return tagDir is not null && FindSubDirectory(tagDir, articleName) is not null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task CreateProductDirectoryAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryTagName);
        ArgumentException.ThrowIfNullOrWhiteSpace(articleName);
        EnsureSinglePathSegment(primaryTagName, nameof(primaryTagName));
        EnsureSinglePathSegment(articleName, nameof(articleName));

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            if (tagDir is null)
            {
                var tagPath = GetSafeChildPath(rootDir.FullName, primaryTagName);
                tagDir = Directory.CreateDirectory(tagPath);
            }

            if (FindSubDirectory(tagDir, articleName) is null)
            {
                var productPath = GetSafeChildPath(tagDir.FullName, articleName);
                Directory.CreateDirectory(productPath);
                _logger.LogInformation(
                    "Created product directory '{PrimaryTag}/{Article}' on disk",
                    tagDir.Name,
                    articleName
                );
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<StorageRenameResult> RenameProductDirectoryAsync(
        string primaryTagName,
        string currentArticleName,
        string newArticleName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            return RenameProductDirectoryInternal(
                rootDir,
                primaryTagName,
                currentArticleName,
                newArticleName
            );
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<StorageRenameResult> MoveProductDirectoryAsync(
        string sourcePrimaryTagName,
        string sourceArticleName,
        string targetPrimaryTagName,
        string targetArticleName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var sourceTagDir = FindSubDirectory(rootDir, sourcePrimaryTagName);
            var productDir = sourceTagDir is null
                ? null
                : FindSubDirectory(sourceTagDir, sourceArticleName);

            if (sourceTagDir is null || productDir is null)
                return new StorageRenameResult(
                    StorageRenameStatus.SourceNotFound,
                    sourceArticleName,
                    sourceArticleName,
                    $"Source product '{sourcePrimaryTagName}/{sourceArticleName}' was not found."
                );

            if (
                !FileSystemNameValidator.IsValid(targetPrimaryTagName)
                || !FileSystemNameValidator.IsValid(targetArticleName)
            )
                return new StorageRenameResult(
                    StorageRenameStatus.InvalidTargetName,
                    productDir.Name,
                    productDir.Name,
                    "Target primary tag or article name is invalid for file system."
                );

            var targetTagDir = FindSubDirectory(rootDir, targetPrimaryTagName);
            if (
                targetTagDir is not null
                && string.Equals(sourceTagDir.Name, targetTagDir.Name, StringComparison.Ordinal)
            )
            {
                if (
                    !string.Equals(
                        sourceTagDir.Name,
                        targetPrimaryTagName,
                        StringComparison.Ordinal
                    )
                    && sourceTagDir.EnumerateDirectories().Count(d => !IsTempName(d.Name)) == 1
                )
                {
                    var tagRename = RenameDirectorySafe(
                        rootDir,
                        sourceTagDir,
                        targetPrimaryTagName
                    );
                    if (!tagRename.IsSuccess)
                        return tagRename;
                }

                return RenameProductDirectoryInternal(
                    rootDir,
                    targetPrimaryTagName,
                    sourceArticleName,
                    targetArticleName
                );
            }

            if (targetTagDir is null)
            {
                var targetTagPath = GetSafeChildPath(rootDir.FullName, targetPrimaryTagName);
                targetTagDir = Directory.CreateDirectory(targetTagPath);
            }

            var conflicting = FindConflictingEntryName(
                targetTagDir,
                string.Empty,
                targetArticleName
            );
            if (conflicting is not null)
            {
                _logger.LogWarning(
                    "Collision while moving product '{SourceTag}/{SourceArticle}' to '{TargetTag}/{TargetArticle}'",
                    sourceTagDir.Name,
                    productDir.Name,
                    targetTagDir.Name,
                    targetArticleName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.TargetCollision,
                    productDir.Name,
                    productDir.Name,
                    $"Product '{conflicting}' already exists in '{targetTagDir.Name}'."
                );
            }

            var previousArticle = productDir.Name;
            var destinationPath = GetSafeChildPath(targetTagDir.FullName, targetArticleName);

            try
            {
                Directory.Move(productDir.FullName, destinationPath);
                _logger.LogInformation(
                    "Moved product directory on disk '{SourceTag}/{SourceArticle}' -> '{TargetTag}/{TargetArticle}'",
                    sourceTagDir.Name,
                    previousArticle,
                    targetTagDir.Name,
                    targetArticleName
                );

                return new StorageRenameResult(
                    StorageRenameStatus.Success,
                    previousArticle,
                    targetArticleName
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(
                    ex,
                    "I/O error while moving product directory '{SourceTag}/{SourceArticle}' to '{TargetTag}/{TargetArticle}'",
                    sourceTagDir.Name,
                    previousArticle,
                    targetTagDir.Name,
                    targetArticleName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.IoError,
                    previousArticle,
                    previousArticle,
                    ex.Message
                );
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> DeleteProductDirectoryAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            var productDir = tagDir is null ? null : FindSubDirectory(tagDir, articleName);
            if (tagDir is null || productDir is null)
                return false;

            var tagActualName = tagDir.Name;
            var productActualName = productDir.Name;
            productDir.Delete(recursive: true);

            _logger.LogInformation(
                "Deleted product directory '{PrimaryTag}/{Article}' from disk",
                tagActualName,
                productActualName
            );
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<ProductIdReadResult> ReadProductIdAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var productDir = GetRequiredProductDirectory(rootDir, primaryTagName, articleName);
            var idFileName = GetProductIdFileName();
            var file = FindFile(productDir, idFileName);

            if (file is null)
                return new ProductIdReadResult(ProductIdReadStatus.NotFound, null);

            var rawText = (
                await File.ReadAllTextAsync(file.FullName, Encoding.UTF8, cancellationToken)
            ).Trim();

            if (Guid.TryParse(rawText, out var parsedId) && parsedId != Guid.Empty)
                return new ProductIdReadResult(ProductIdReadStatus.Found, parsedId, rawText);

            _logger.LogWarning(
                "Invalid .typoerp_id content on disk in '{PrimaryTag}/{Article}': '{RawContent}'",
                primaryTagName,
                articleName,
                rawText
            );
            return new ProductIdReadResult(ProductIdReadStatus.InvalidContent, null, rawText);
        }
        finally
        {
            _mutex.Release();
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

    public async Task<IReadOnlyList<StoredProductFileInfo>> GetProductFilesAsync(
        string primaryTagName,
        string articleName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var productDir = GetRequiredProductDirectory(rootDir, primaryTagName, articleName);
            var fileEntries = productDir
                .EnumerateFiles()
                .Where(f => !IsTempName(f.Name))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToList();

            var result = new List<StoredProductFileInfo>(fileEntries.Count);
            foreach (var file in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    StreamBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );
                var hashResult = await ProductFileHasher.ComputeAsync(stream, cancellationToken);
                result.Add(
                    new StoredProductFileInfo(file.Name, hashResult.SizeBytes, hashResult.HashHex)
                );
            }

            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<StoredProductFileInfo?> GetProductFileInfoAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            var productDir = tagDir is null ? null : FindSubDirectory(tagDir, articleName);
            var file = productDir is null ? null : FindFile(productDir, fileName);

            if (file is null)
                return null;

            await using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            var hashResult = await ProductFileHasher.ComputeAsync(stream, cancellationToken);
            return new StoredProductFileInfo(file.Name, hashResult.SizeBytes, hashResult.HashHex);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<Stream> OpenReadProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var productDir = GetRequiredProductDirectory(rootDir, primaryTagName, articleName);
            var file = FindFile(productDir, fileName);
            if (file is null)
                throw new FileNotFoundException(
                    $"File '{fileName}' was not found in '{primaryTagName}/{articleName}'.",
                    fileName
                );

            return new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
        }
        finally
        {
            _mutex.Release();
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
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        EnsureSinglePathSegment(fileName, nameof(fileName));

        if (content.CanSeek && content.Position != 0)
            content.Position = 0;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var productDir = GetRequiredProductDirectory(rootDir, primaryTagName, articleName);
            var existing = FindFile(productDir, fileName);
            var actualFileName = existing?.Name ?? fileName;
            var filePath = GetSafeChildPath(productDir.FullName, actualFileName);

            var hasher = new XxHash64();
            var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferSize);
            long totalBytes = 0;

            try
            {
                await using var fileStream = new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    StreamBufferSize,
                    FileOptions.Asynchronous
                );

                int bytesRead;
                while (
                    (
                        bytesRead = await content.ReadAsync(
                            buffer.AsMemory(0, StreamBufferSize),
                            cancellationToken
                        )
                    ) > 0
                )
                {
                    hasher.Append(buffer.AsSpan(0, bytesRead));
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytes += bytesRead;
                }

                await fileStream.FlushAsync(cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var hashHex = ProductFileHasher.FormatHashHex(hasher.GetCurrentHashAsUInt64());

            _logger.LogInformation(
                "Wrote file to disk '{PrimaryTag}/{Article}/{FileName}' ({SizeBytes} bytes, XxHash64={Hash})",
                primaryTagName,
                articleName,
                actualFileName,
                totalBytes,
                hashHex
            );

            return new StoredProductFileInfo(actualFileName, totalBytes, hashHex);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<StoredProductFileInfo> WriteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(content);
        using var ms = new MemoryStream(content, writable: false);
        return await WriteProductFileAsync(
            primaryTagName,
            articleName,
            fileName,
            ms,
            cancellationToken
        );
    }

    public async Task<StorageRenameResult> RenameProductFileAsync(
        string primaryTagName,
        string articleName,
        string currentFileName,
        string newFileName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            var productDir = tagDir is null ? null : FindSubDirectory(tagDir, articleName);
            var sourceFile = productDir is null ? null : FindFile(productDir, currentFileName);

            if (tagDir is null || productDir is null || sourceFile is null)
                return new StorageRenameResult(
                    StorageRenameStatus.SourceNotFound,
                    currentFileName,
                    currentFileName,
                    $"File '{currentFileName}' was not found in '{primaryTagName}/{articleName}'."
                );

            if (string.Equals(sourceFile.Name, newFileName, StringComparison.Ordinal))
                return new StorageRenameResult(
                    StorageRenameStatus.NoChangeNeeded,
                    sourceFile.Name,
                    sourceFile.Name
                );

            if (!FileSystemNameValidator.IsValid(newFileName))
            {
                _logger.LogWarning(
                    "Cannot rename file '{PrimaryTag}/{Article}/{CurrentFile}' to invalid name '{NewFile}'",
                    tagDir.Name,
                    productDir.Name,
                    sourceFile.Name,
                    newFileName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.InvalidTargetName,
                    sourceFile.Name,
                    sourceFile.Name,
                    $"Target file name '{newFileName}' is invalid for file system."
                );
            }

            var conflictingEntryName = FindConflictingEntryName(
                productDir,
                sourceFile.Name,
                newFileName
            );
            if (conflictingEntryName is not null)
            {
                _logger.LogWarning(
                    "Collision while renaming file '{PrimaryTag}/{Article}/{CurrentFile}' to '{NewFile}': '{ExistingFile}' already exists",
                    tagDir.Name,
                    productDir.Name,
                    sourceFile.Name,
                    newFileName,
                    conflictingEntryName
                );
                return new StorageRenameResult(
                    StorageRenameStatus.TargetCollision,
                    sourceFile.Name,
                    sourceFile.Name,
                    $"File '{conflictingEntryName}' already exists in '{tagDir.Name}/{productDir.Name}'."
                );
            }

            var previousName = sourceFile.Name;
            var renameResult = RenameFileSafe(productDir, sourceFile, newFileName);
            if (renameResult.IsSuccess)
            {
                _logger.LogInformation(
                    "Renamed file on disk '{PrimaryTag}/{Article}/{OldFile}' -> '{NewFile}'",
                    tagDir.Name,
                    productDir.Name,
                    previousName,
                    newFileName
                );
            }

            return renameResult;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> DeleteProductFileAsync(
        string primaryTagName,
        string articleName,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var rootDir = GetRequiredRootDirectory();
            var tagDir = FindSubDirectory(rootDir, primaryTagName);
            var productDir = tagDir is null ? null : FindSubDirectory(tagDir, articleName);
            var file = productDir is null ? null : FindFile(productDir, fileName);

            if (productDir is null || file is null)
                return false;

            var actualName = file.Name;
            file.Delete();
            _logger.LogInformation(
                "Deleted file '{PrimaryTag}/{Article}/{FileName}' from disk",
                primaryTagName,
                articleName,
                actualName
            );
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private StorageRenameResult RenameProductDirectoryInternal(
        DirectoryInfo rootDir,
        string primaryTagName,
        string currentArticleName,
        string newArticleName
    )
    {
        var tagDir = FindSubDirectory(rootDir, primaryTagName);
        var productDir = tagDir is null ? null : FindSubDirectory(tagDir, currentArticleName);
        if (tagDir is null || productDir is null)
        {
            _logger.LogWarning(
                "Cannot rename product '{PrimaryTag}/{CurrentArticle}' to '{NewArticle}': source not found",
                primaryTagName,
                currentArticleName,
                newArticleName
            );
            return new StorageRenameResult(
                StorageRenameStatus.SourceNotFound,
                currentArticleName,
                currentArticleName,
                $"Product directory '{primaryTagName}/{currentArticleName}' was not found."
            );
        }

        if (string.Equals(productDir.Name, newArticleName, StringComparison.Ordinal))
            return new StorageRenameResult(
                StorageRenameStatus.NoChangeNeeded,
                productDir.Name,
                productDir.Name
            );

        if (!FileSystemNameValidator.IsValid(newArticleName))
        {
            _logger.LogWarning(
                "Cannot rename product '{PrimaryTag}/{CurrentArticle}' to invalid name '{NewArticle}'",
                tagDir.Name,
                productDir.Name,
                newArticleName
            );
            return new StorageRenameResult(
                StorageRenameStatus.InvalidTargetName,
                productDir.Name,
                productDir.Name,
                $"Target article name '{newArticleName}' is invalid for file system."
            );
        }

        var conflictingEntryName = FindConflictingEntryName(
            tagDir,
            productDir.Name,
            newArticleName
        );
        if (conflictingEntryName is not null)
        {
            _logger.LogWarning(
                "Collision while renaming product '{PrimaryTag}/{CurrentArticle}' to '{NewArticle}': '{ExistingArticle}' already exists",
                tagDir.Name,
                productDir.Name,
                newArticleName,
                conflictingEntryName
            );
            return new StorageRenameResult(
                StorageRenameStatus.TargetCollision,
                productDir.Name,
                productDir.Name,
                $"Product directory '{conflictingEntryName}' already exists in '{tagDir.Name}'."
            );
        }

        var previousName = productDir.Name;
        var renameResult = RenameDirectorySafe(tagDir, productDir, newArticleName);
        if (renameResult.IsSuccess)
        {
            _logger.LogInformation(
                "Renamed product directory on disk '{PrimaryTag}/{OldArticle}' -> '{NewArticle}'",
                tagDir.Name,
                previousName,
                newArticleName
            );
        }

        return renameResult;
    }

    private StorageRenameResult RenameDirectorySafe(
        DirectoryInfo parentDir,
        DirectoryInfo sourceDir,
        string newName
    )
    {
        var originalName = sourceDir.Name;
        var targetPath = GetSafeChildPath(parentDir.FullName, newName);
        var isCaseOnlyChange = string.Equals(
            originalName,
            newName,
            StringComparison.OrdinalIgnoreCase
        );

        try
        {
            if (isCaseOnlyChange)
            {
                var tempPath = CreateUniqueTempPath(parentDir.FullName);
                Directory.Move(sourceDir.FullName, tempPath);
                try
                {
                    Directory.Move(tempPath, targetPath);
                }
                catch
                {
                    if (Directory.Exists(tempPath) && !Directory.Exists(sourceDir.FullName))
                        Directory.Move(tempPath, sourceDir.FullName);
                    throw;
                }
            }
            else
            {
                Directory.Move(sourceDir.FullName, targetPath);
            }

            return new StorageRenameResult(StorageRenameStatus.Success, originalName, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "I/O error while renaming directory '{OldName}' to '{NewName}' in '{ParentPath}'",
                originalName,
                newName,
                parentDir.FullName
            );
            return new StorageRenameResult(
                StorageRenameStatus.IoError,
                originalName,
                originalName,
                ex.Message
            );
        }
    }

    private StorageRenameResult RenameFileSafe(
        DirectoryInfo parentDir,
        FileInfo sourceFile,
        string newName
    )
    {
        var originalName = sourceFile.Name;
        var targetPath = GetSafeChildPath(parentDir.FullName, newName);
        var isCaseOnlyChange = string.Equals(
            originalName,
            newName,
            StringComparison.OrdinalIgnoreCase
        );

        try
        {
            if (isCaseOnlyChange)
            {
                var tempPath = CreateUniqueTempPath(parentDir.FullName);
                File.Move(sourceFile.FullName, tempPath, overwrite: false);
                try
                {
                    File.Move(tempPath, targetPath, overwrite: false);
                }
                catch
                {
                    if (File.Exists(tempPath) && !File.Exists(sourceFile.FullName))
                        File.Move(tempPath, sourceFile.FullName, overwrite: false);
                    throw;
                }
            }
            else
            {
                File.Move(sourceFile.FullName, targetPath, overwrite: false);
            }

            return new StorageRenameResult(StorageRenameStatus.Success, originalName, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "I/O error while renaming file '{OldName}' to '{NewName}' in '{ParentPath}'",
                originalName,
                newName,
                parentDir.FullName
            );
            return new StorageRenameResult(
                StorageRenameStatus.IoError,
                originalName,
                originalName,
                ex.Message
            );
        }
    }

    private bool IsRootAccessibleInternal()
    {
        if (string.IsNullOrWhiteSpace(_options.RootPath))
            return false;

        try
        {
            if (!Directory.Exists(_options.RootPath))
                return false;

            using var enumerator = Directory
                .EnumerateFileSystemEntries(_options.RootPath)
                .GetEnumerator();
            _ = enumerator.MoveNext();
            return true;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(
                ex,
                "Storage root path '{RootPath}' is not accessible",
                _options.RootPath
            );
            return false;
        }
    }

    private DirectoryInfo GetRequiredRootDirectory()
    {
        if (!IsRootAccessibleInternal())
        {
            _logger.LogError(
                "Products storage root '{RootPath}' does not exist or is not accessible",
                _options.RootPath
            );
            throw new InvalidOperationException(
                "Корневой путь хранилища товаров не существует или недоступен."
            );
        }

        return new DirectoryInfo(Path.GetFullPath(_options.RootPath));
    }

    private static DirectoryInfo GetRequiredProductDirectory(
        DirectoryInfo rootDir,
        string primaryTagName,
        string articleName
    )
    {
        var tagDir = FindSubDirectory(rootDir, primaryTagName);
        var productDir = tagDir is null ? null : FindSubDirectory(tagDir, articleName);
        if (tagDir is null || productDir is null)
            throw new DirectoryNotFoundException(
                $"Product directory '{primaryTagName}/{articleName}' does not exist."
            );

        return productDir;
    }

    private static DirectoryInfo? FindSubDirectory(DirectoryInfo parentDir, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var directories = parentDir.EnumerateDirectories().Where(d => !IsTempName(d.Name)).ToList();

        return directories.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.Ordinal)
            )
            ?? directories.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static FileInfo? FindFile(DirectoryInfo parentDir, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var files = parentDir.EnumerateFiles().Where(f => !IsTempName(f.Name)).ToList();

        return files.FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.Ordinal))
            ?? files.FirstOrDefault(f =>
                string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string? FindConflictingEntryName(
        DirectoryInfo parentDir,
        string sourceExactName,
        string targetName
    )
    {
        foreach (var entry in parentDir.EnumerateFileSystemInfos())
        {
            if (IsTempName(entry.Name))
                continue;

            if (string.Equals(entry.Name, sourceExactName, StringComparison.Ordinal))
                continue;

            if (string.Equals(entry.Name, targetName, StringComparison.OrdinalIgnoreCase))
                return entry.Name;
        }

        return null;
    }

    private static string CreateUniqueTempPath(string parentDirectoryPath)
    {
        while (true)
        {
            var tempName = $"{TempRenamePrefix}{Guid.NewGuid():N}";
            var tempPath = Path.Combine(parentDirectoryPath, tempName);
            if (!File.Exists(tempPath) && !Directory.Exists(tempPath))
                return tempPath;
        }
    }

    private static bool IsTempName(string name)
    {
        return name.StartsWith(TempRenamePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSinglePathSegment(string segment, string paramName)
    {
        if (
            segment.Contains('/')
            || segment.Contains('\\')
            || string.Equals(segment.Trim(), ".", StringComparison.Ordinal)
            || string.Equals(segment.Trim(), "..", StringComparison.Ordinal)
        )
            throw new ArgumentException(
                $"Name '{segment}' cannot contain path separators or relative traversal segments.",
                paramName
            );
    }

    private static string GetSafeChildPath(string parentFullPath, string childName)
    {
        EnsureSinglePathSegment(childName, nameof(childName));

        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentFullPath));
        var combinedFull = Path.GetFullPath(Path.Combine(normalizedParent, childName));
        var actualParentOfChild = Path.GetDirectoryName(combinedFull);

        if (
            actualParentOfChild is null
            || !string.Equals(
                Path.TrimEndingDirectorySeparator(actualParentOfChild),
                normalizedParent,
                StringComparison.Ordinal
            )
        )
            throw new InvalidOperationException(
                $"Resolved path '{combinedFull}' escapes parent directory '{normalizedParent}'."
            );

        return combinedFull;
    }

    private string GetProductIdFileName()
    {
        return string.IsNullOrWhiteSpace(_options.ProductIdFileName)
            ? ProductsStorageOptions.DefaultProductIdFileName
            : _options.ProductIdFileName.Trim();
    }
}
