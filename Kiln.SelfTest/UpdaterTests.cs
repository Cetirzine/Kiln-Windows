using Kiln.Core;
using System.Text.Json;

internal static class UpdaterTests
{
    internal static void Run(Action<string, bool> check)
    {
        Json Release(bool draft = false, bool preview = false, string asset = "Kiln-Windows-x64-v0.2.14w.zip") => Json.Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v0.2.14w", draft, prerelease = preview, html_url = "https://github.com/Cetirzine/Kiln-Windows/releases/tag/v0.2.14w",
            body = "Release notes", assets = new[] { new { name = asset, browser_download_url = "https://github.com/Cetirzine/Kiln-Windows/releases/download/v0.2.14w/" + asset, digest = "sha256:" + new string('a', 64), size = 12345 } },
        }));
        var release = Updater.ParseRelease(Release(), "0.2.13w");
        check("Updater selects the exact Windows ZIP and GitHub SHA-256", release is { Version: "0.2.14w", Size: 12345, Sha256.Length: 64 } && release.ZipName == "Kiln-Windows-x64-v0.2.14w.zip");
        check("Updater does not offer the current version", Updater.ParseRelease(Release(), "0.2.14w") == null);
        check("Updater never downgrades to an older release", Updater.ParseRelease(Release(), "0.2.15w") == null);
        check("Updater ignores draft releases", Updater.ParseRelease(Release(draft: true), "0.2.13w") == null);
        check("Updater ignores prereleases", Updater.ParseRelease(Release(preview: true), "0.2.13w") == null);
        check("Updater does not treat the installer EXE as an update ZIP", Updater.ParseRelease(Release(asset: "Kiln-Windows-x64-v0.2.14w-Setup.exe"), "0.2.13w") == null);
        var page = new Uri("https://github.com/Cetirzine/Kiln-Windows/releases/tag/v0.2.14w");
        var website = Updater.ParseWebsiteRelease(page, new string('A', 64) + "  Kiln-Windows-x64-v0.2.14w.zip", "0.2.13w");
        check("Official website fallback validates the named ZIP checksum", website?.Sha256 == new string('a', 64) && website.Version == "0.2.14w");
        check("Official website fallback refuses repeat or older versions", Updater.ParseWebsiteRelease(page, "", "0.2.14w") == null && Updater.ParseWebsiteRelease(page, "", "0.2.15w") == null);
        foreach (var (name, url, checksum) in new[]
        {
            ("wrong repository", "https://github.com/example/Other/releases/tag/v0.2.14w", new string('a', 64)),
            ("wrong host", "https://github.com.example.invalid/Cetirzine/Kiln-Windows/releases/tag/v0.2.14w", new string('a', 64)),
            ("wrong asset", page.AbsoluteUri, new string('a', 64) + "  Other.zip"),
            ("malformed digest", page.AbsoluteUri, "invalid"),
        })
        {
            try { Updater.ParseWebsiteRelease(new Uri(url), checksum, "0.2.13w"); check("Website update fallback rejects " + name, false); }
            catch (LauncherException) { check("Website update fallback rejects " + name, true); }
        }
        foreach (var digest in new string?[] { null, new string('z', 64) })
        {
            try { Updater.Prepare(release! with { Sha256 = digest }, Path.Combine(Path.GetTempPath(), "kiln-update-invalid-" + Guid.NewGuid().ToString("N"), "Kiln")).GetAwaiter().GetResult(); check("Updater requires a valid digest: " + (digest == null ? "missing" : "malformed"), false); }
            catch (LauncherException e) { check("Updater requires a valid digest: " + (digest == null ? "missing" : "malformed"), e.Message.Contains("SHA-256", StringComparison.Ordinal)); }
        }
    }
}
