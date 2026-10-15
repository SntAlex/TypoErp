using System.Text;
using Shouldly;
using TypoErp.Modules.Products.Storage;

namespace TypoErp.Modules.Products.Tests.Storage;

public sealed class ProductFileHasherTests
{
    [Fact]
    public async Task ComputeAsync_ShouldProduceSameXxHash64AndSizeAsByteSpan()
    {
        var bytes = Encoding.UTF8.GetBytes("TypoErp printing file payload 2026");
        using var stream = new MemoryStream(bytes);

        var fromSpan = ProductFileHasher.Compute(bytes);
        var fromStream = await ProductFileHasher.ComputeAsync(stream);

        fromStream.SizeBytes.ShouldBe(bytes.Length);
        fromStream.Hash.ShouldBe(fromSpan.Hash);
        fromStream.HashHex.ShouldBe(fromSpan.HashHex);
        fromStream.HashHex.Length.ShouldBe(16);
    }

    [Fact]
    public void Compute_WhenPayloadsDiffer_ShouldProduceDifferentHashes()
    {
        var hash1 = ProductFileHasher.Compute("file_content_v1"u8);
        var hash2 = ProductFileHasher.Compute("file_content_v2"u8);

        hash1.Hash.ShouldNotBe(hash2.Hash);
        hash1.HashHex.ShouldNotBe(hash2.HashHex);
    }

    [Fact]
    public void Compute_WhenEmptyPayload_ShouldReturnZeroSizeAndValid16CharHex()
    {
        var result = ProductFileHasher.Compute(ReadOnlySpan<byte>.Empty);

        result.SizeBytes.ShouldBe(0);
        result.HashHex.Length.ShouldBe(16);
    }
}
