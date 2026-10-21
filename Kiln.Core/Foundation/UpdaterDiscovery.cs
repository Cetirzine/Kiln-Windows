using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kiln.Core;

public static partial class Updater
{
    // Official /releases/latest redirects to the latest stable tag without using the
    // REST API quota. Checksums still come from this repository's HTTPS release assets.
    public static async Task<Release?> CheckWebsite(string currentVersion, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Head, $"https://github.com/{Repository}/releases/latest");
        using var response = await Net.Session.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
        var page = response.RequestMessage!.RequestUri!; var coordinates = WebsiteCoordinates(page);
        if (!IsNewer(coordinates.Version, currentVersion)) return null;
        var checksum = Encoding.UTF8.GetString(await Net.data(new[] { coordinates.Zip + ".sha256" }, ct: timeout.Token));
        var release = ParseWebsiteRelease(page, checksum, currentVersion)!;
        try
        {
            using var assetRequest = new HttpRequestMessage(HttpMethod.Head, release.ZipUrl);
            using var asset = await Net.Session.SendAsync(assetRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (asset.IsSuccessStatusCode) release = release with { Size = asset.Content.Headers.ContentLength };
        }
        catch (Exception error) when (!ct.IsCancellationRequested) { Log.log("[Update] Asset size unavailable: " + error.Friendly()); }
        try
        {
            var feed = XDocument.Parse(Encoding.UTF8.GetString(await Net.data(new[] { $"https://github.com/{Repository}/releases.atom" }, ct: timeout.Token)));
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var entry = feed.Root?.Elements(atom + "entry").FirstOrDefault(e => e.Elements(atom + "link").Any(l => l.Attribute("href")?.Value == page.AbsoluteUri));
            if (entry?.Element(atom + "content")?.Value is { } html)
            {
                var notes = Regex.Replace(html, @"(?i)</(?:p|li|h[1-6])\s*>", "\n");
                notes = WebUtility.HtmlDecode(Regex.Replace(notes, "<[^>]+>", "")).Trim();
                release = release with { Notes = notes };
            }
        }
        catch (Exception error) when (!ct.IsCancellationRequested) { Log.log("[Update] Release feed unavailable: " + error.Friendly()); }
        ct.ThrowIfCancellationRequested(); return release;
    }

    public static Release? ParseWebsiteRelease(Uri page, string checksum, string currentVersion)
    {
        var coordinates = WebsiteCoordinates(page);
        if (!IsNewer(coordinates.Version, currentVersion)) return null;
        var parts = checksum.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit) || parts.Length == 2 && parts[1].TrimStart('*') != coordinates.Name)
            throw Verification(T.Invoke("发布包的 SHA-256 文件无效", "The release SHA-256 file is invalid"));
        return new(coordinates.Version, "", page.AbsoluteUri, coordinates.Zip, coordinates.Name, parts[0].ToLowerInvariant(), null);
    }

    private static (string Version, string Name, string Zip) WebsiteCoordinates(Uri page)
    {
        var prefix = "/" + Repository + "/releases/tag/";
        if (page.Scheme != "https" || !page.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !page.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Verification(T.Invoke("发布页地址不符", "Unexpected release page"));
        var tag = Uri.UnescapeDataString(page.AbsolutePath[prefix.Length..]);
        if (!Regex.IsMatch(tag, @"^v?[0-9][A-Za-z0-9._-]{0,63}$")) throw Verification(T.Invoke("发布版本号无效", "Invalid release tag"));
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        var name = $"Kiln-Windows-x64-v{version}.zip";
        return (version, name, $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}");
    }
}
