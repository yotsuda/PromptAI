using System.Net.Http;

namespace PromptAI.Cmdlets;

/// <summary>
/// Loads an image from a local file path or HTTPS URL into a base64-encoded
/// payload plus its MIME type. Used by the per-provider cmdlets to attach
/// image input to chat requests.
/// </summary>
internal static class ImageLoader
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// Result of loading one image. <see cref="OriginalUrl"/> is set only when the
    /// input was an HTTPS URL — providers that accept URL inputs (Anthropic, OpenAI)
    /// can use it directly and skip the download. <see cref="Base64"/> is computed
    /// lazily by <see cref="EnsureBase64"/> for providers that must inline bytes
    /// (Gemini).
    /// </summary>
    public class LoadedImage
    {
        public string MimeType { get; }
        public string? OriginalUrl { get; }
        private byte[]? _bytes;
        private string? _base64;

        internal LoadedImage(string mimeType, string? originalUrl, byte[]? bytes)
        {
            MimeType = mimeType;
            OriginalUrl = originalUrl;
            _bytes = bytes;
        }

        /// <summary>
        /// Returns base64-encoded bytes, fetching the URL if necessary. Local-file
        /// inputs already have bytes populated; URL inputs trigger an HTTPS GET here.
        /// </summary>
        public string EnsureBase64()
        {
            if (_base64 != null) return _base64;

            if (_bytes == null && OriginalUrl != null)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, OriginalUrl);
                req.Headers.UserAgent.ParseAdd("PromptAI/0.1.3 (+https://github.com/yotsuda/PromptAI)");
                using var resp = s_httpClient.Send(req);
                resp.EnsureSuccessStatusCode();
                using var ms = new MemoryStream();
                resp.Content.ReadAsStream().CopyTo(ms);
                _bytes = ms.ToArray();
            }

            _base64 = Convert.ToBase64String(_bytes ?? throw new InvalidOperationException("No image bytes available."));
            return _base64;
        }
    }

    public static LoadedImage Load(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            throw new ArgumentException("Image path or URL is empty.", nameof(pathOrUrl));

        bool isUrl = pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (isUrl)
        {
            // Defer the download — providers that accept URL inputs (Claude, OpenAI)
            // pass it through. Gemini calls EnsureBase64() to materialize.
            return new LoadedImage(GuessMime(pathOrUrl), pathOrUrl, bytes: null);
        }

        if (!File.Exists(pathOrUrl))
            throw new FileNotFoundException($"Image file not found: {pathOrUrl}", pathOrUrl);
        var bytes = File.ReadAllBytes(pathOrUrl);
        return new LoadedImage(GuessMime(pathOrUrl), originalUrl: null, bytes: bytes);
    }

    private static string GuessMime(string pathOrUrl)
    {
        var ext = Path.GetExtension(pathOrUrl).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png"           => "image/png",
            "gif"           => "image/gif",
            "webp"          => "image/webp",
            "bmp"           => "image/bmp",
            _               => "image/jpeg",  // sensible default for content-type-less URLs
        };
    }
}
