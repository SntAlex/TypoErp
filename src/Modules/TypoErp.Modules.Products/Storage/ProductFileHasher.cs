using System.Buffers;
using System.IO.Hashing;

namespace TypoErp.Modules.Products.Storage;

/// <summary>
///     Вычисляет 64-битный непотоковый и потоковый хэш XxHash64 и размер файла в байтах.
/// </summary>
public static class ProductFileHasher
{
    private const int DefaultBufferSize = 64 * 1024;

    public static ProductFileHashResult Compute(ReadOnlySpan<byte> content)
    {
        var hash = XxHash64.HashToUInt64(content);
        return new ProductFileHashResult(hash, FormatHashHex(hash), content.Length);
    }

    public static async Task<ProductFileHashResult> ComputeAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        var hasher = new XxHash64();
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        long totalBytes = 0;

        try
        {
            int bytesRead;
            while (
                (
                    bytesRead = await stream.ReadAsync(
                        buffer.AsMemory(0, DefaultBufferSize),
                        cancellationToken
                    )
                ) > 0
            )
            {
                hasher.Append(buffer.AsSpan(0, bytesRead));
                totalBytes += bytesRead;
            }

            var hash = hasher.GetCurrentHashAsUInt64();
            return new ProductFileHashResult(hash, FormatHashHex(hash), totalBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string FormatHashHex(ulong hash)
    {
        return hash.ToString("x16");
    }
}
