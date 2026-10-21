using Kiln.Core;

internal static class NetworkSmoke
{
    public static async Task<int> Run()
    {
        var failures = 0;
        foreach (var query in new[] { "", "sodium", "Sodium Extra" })
        {
            try
            {
                var results = await Modrinth.Search(query, Modrinth.ProjectType.Mod, "26.2", "Fabric");
                var valid = results.Count > 0 && results.All(p => !string.IsNullOrWhiteSpace(p.Id));
                Console.WriteLine($"{(valid ? "[OK]" : "[FAIL]")} Modrinth query '{query}': {results.Count} results"); if (!valid) failures++;
            }
            catch (Exception e) { Console.WriteLine("[FAIL] Modrinth: " + e.Friendly()); failures++; }
        }
        // Exercise the same version lookup and verified download used by the UI, in an isolated temp directory.
        var folder = Path.Combine(Path.GetTempPath(), "Kiln-network-" + Guid.NewGuid().ToString("N"));
        try
        {
            var projects = await Modrinth.Search("Sodium Extra", Modrinth.ProjectType.Mod, "26.2", "Fabric");
            var project = projects.First(p => p.Slug == "sodium-extra");
            var versions = await Modrinth.Versions(project.Id, "26.2", "Fabric");
            var version = versions.First(v => v.FileUrl.Length > 0 && v.Sha1 != null);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Path.GetFileName(version.FileName));
            var item = new DownloadItem(version.FileUrl, path, version.Sha1, version.Size);
            await Downloader.Run(new[] { item });
            var valid = File.Exists(path) && Hash.Sha1File(path) == version.Sha1;
            Console.WriteLine($"{(valid ? "[OK]" : "[FAIL]")} Search → versions → download → SHA1: {version.FileName} ({new FileInfo(path).Length} bytes)");
            if (!valid) failures++;
            var instanceDir = Path.Combine(folder, "versions", "Export network fixture"); Directory.CreateDirectory(instanceDir);
            File.WriteAllText(Path.Combine(instanceDir, "Export network fixture.json"), "{\"id\":\"26.2\",\"mainClass\":\"test\",\"type\":\"release\",\"releaseTime\":\"2026-06-16T00:00:00Z\"}");
            var exportInstance = new McInstance(folder, "Export network fixture"); var setup = exportInstance.Setup; setup.Isolation = true; LauncherSettings.Shared.SaveInstanceSetup(setup, exportInstance.Dir);
            var mods = Path.Combine(exportInstance.GameDir, "mods"); Directory.CreateDirectory(mods); File.Copy(path, Path.Combine(mods, Path.GetFileName(path)));
            var packPath = Path.Combine(folder, "network-export.mrpack");
            var exported = await Modpack.Export(exportInstance, "Network export", "1.2.3", "Export smoke test", new HashSet<string> { "mods" }, packPath, new Job("Export network fixture"));
            var pack = Modpack.Inspect(packPath); var entry = pack.Manifest["files"].Array.Single();
            if (exported.Linked != 1 || exported.Embedded != 0 || entry["hashes"]["sha1"].String != version.Sha1 || entry["hashes"]["sha512"].String is not { Length: 128 } || Zip.List(packPath).Any(p => p.StartsWith("overrides/mods/"))) throw new Exception("Export did not link the known Modrinth file");
            Console.WriteLine("[OK] Export known Modrinth mod → download link, SHA1/SHA512 manifest, no duplicate JAR");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await Modrinth.Search("cancel", Modrinth.ProjectType.Mod, null, null, ct: cancellation.Token); Console.WriteLine("[FAIL] Cancelled search completed"); failures++; }
            catch (OperationCanceledException) { Console.WriteLine("[OK] Superseded search cancels without an error dialog"); }
        }
        catch (Exception e) { Console.WriteLine("[FAIL] Download flow: " + e.Friendly()); failures++; }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        var cfFolder = Path.Combine(Path.GetTempPath(), "Kiln-cf-network-" + Guid.NewGuid().ToString("N"));
        try
        {
            var results = await CurseForge.Search("jei", Modrinth.ProjectType.Mod, "1.12.2", "Forge");
            var project = results.First(p => p.Id == "cf-238222");
            Console.WriteLine($"[OK] CurseForge search: {results.Count} results, found JEI");
            var versions = await CurseForge.Files(project, "1.12.2", "Forge"); var version = versions.First(v => !v.Blocked && v.Sha1 != null);
            var projects = await CurseForge.ProjectsByIds(new[] { 238222 });
            var fileId = int.Parse(version.Id);
            var files = await CurseForge.FilesByIds(new[] { fileId });
            if (!projects.ContainsKey(238222) || !files.ContainsKey(fileId)) throw new Exception("Project/file resolution failed");
            Console.WriteLine("[OK] CurseForge project/dependency and modpack manifest file resolution");
            Directory.CreateDirectory(cfFolder); var path = Path.Combine(cfFolder, Path.GetFileName(version.FileName));
            await Downloader.Run(new[] { new DownloadItem(version.FileUrl, path, version.Sha1, version.Size) { Urls = CurseForge.DownloadUrls(version) } });
            if (Hash.Sha1File(path) != version.Sha1) throw new Exception("CurseForge SHA1 mismatch");
            Console.WriteLine($"[OK] CurseForge search → versions → download → SHA1: {version.FileName} ({new FileInfo(path).Length} bytes)");
            var fingerprints = await CurseForge.Match(new[] { CurseForge.Fingerprint(await File.ReadAllBytesAsync(path)) });
            if (!fingerprints.Values.Any(v => v.Id == version.Id)) throw new Exception("Downloaded CurseForge mod fingerprint was not identified");
            Console.WriteLine("[OK] CurseForge local mod identification by fingerprint");
        }
        catch (Exception e) { Console.WriteLine("[FAIL] CurseForge: " + e.Friendly()); failures++; }
        finally { if (Directory.Exists(cfFolder)) Directory.Delete(cfFolder, true); }
        Console.WriteLine($"Network smoke: {failures} failures. Downloads verified in temporary folders and removed; no installations or account changes."); return failures == 0 ? 0 : 1;
    }
}
