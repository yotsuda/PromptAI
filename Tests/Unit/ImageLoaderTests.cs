using PromptAI.Cmdlets;
using Xunit;

namespace PromptAI.Tests.Unit;

public class ImageLoaderTests
{
    private static string WriteTempImage(string ext, byte[]? bytes = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"promptai-test-{Guid.NewGuid():N}.{ext}");
        File.WriteAllBytes(path, bytes ?? new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header bytes (any payload OK for our tests)
        return path;
    }

    [Fact]
    public void Load_LocalPngFile_PopulatesBytesAndMimeAndNoOriginalUrl()
    {
        var path = WriteTempImage("png");
        try
        {
            var img = ImageLoader.Load(path);
            Assert.Equal("image/png", img.MimeType);
            Assert.Null(img.OriginalUrl);
            Assert.Equal(Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), img.EnsureBase64());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("jpg",  "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png",  "image/png")]
    [InlineData("gif",  "image/gif")]
    [InlineData("webp", "image/webp")]
    [InlineData("bmp",  "image/bmp")]
    [InlineData("xyz",  "image/jpeg")] // unknown extension falls through to jpeg default
    public void Load_LocalFileMimeFromExtension(string ext, string expectedMime)
    {
        var path = WriteTempImage(ext);
        try
        {
            var img = ImageLoader.Load(path);
            Assert.Equal(expectedMime, img.MimeType);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NonExistentLocalFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => ImageLoader.Load("C:\\nonexistent\\image.png"));
    }

    [Fact]
    public void Load_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => ImageLoader.Load(""));
        Assert.Throws<ArgumentException>(() => ImageLoader.Load("   "));
    }

    [Fact]
    public void Load_HttpsUrl_DefersDownload()
    {
        // URL inputs should NOT make a network call at Load() time — the download
        // happens lazily inside EnsureBase64(). We verify by passing a URL that
        // would 404 if actually fetched, and confirming Load returns successfully
        // with the URL recorded but no bytes.
        var img = ImageLoader.Load("https://example.invalid/image.png");
        Assert.Equal("https://example.invalid/image.png", img.OriginalUrl);
        Assert.Equal("image/png", img.MimeType);
    }

    [Fact]
    public void Load_UrlWithoutExtension_FallsBackToJpegMime()
    {
        var img = ImageLoader.Load("https://example.invalid/api/random");
        Assert.Equal("image/jpeg", img.MimeType);
    }
}
