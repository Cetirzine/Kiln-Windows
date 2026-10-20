using Kiln.Core;
using Kiln.Core.DotScene;
using System.Text.Json;

internal static class ParityTests
{
    public static Task<int> Run()
    {
        var passed = 0; var failed = 0;
        void Check(string name, bool condition) { Console.WriteLine($"{(condition ? "[OK]" : "[FAIL]")} {name}"); if (condition) passed++; else failed++; }
        void Throws(string name, Action action) { try { action(); Check(name, false); } catch { Check(name, true); } }
        Check("Offline UUID matches Minecraft server", Account.OfflineUuid("Notch") == "b50ad385829d3141a2167e7d7539ba7f");
        Check("Offline rename changes UUID", Account.OfflineUuid("Steve") != Account.OfflineUuid("Alex"));
        var cancelledJob = new Job("Cancellation test"); cancelledJob.Cancel();
        var cancelled = false;
        try { Downloader.Run(Array.Empty<DownloadItem>(), cancelledJob).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Check("Cancelled job stops downloads", cancelled);
        foreach (var host in new[] { "edge.forgecdn.net", "mediafilez.forgecdn.net", "mediafiles.forgecdn.net" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://" + host + "/files/test.jar");
            CurseForge.AuthenticateDownload(request, "kiln-test-key");
            Check("CurseForge CDN authentication: " + host, request.Headers.TryGetValues("x-api-key", out var values) && values.Single() == "kiln-test-key");
        }
        foreach (var target in new[] { "https://mod.mcimirror.top/files/test.jar", "https://cdn.modrinth.com/test.jar", "http://edge.forgecdn.net/test.jar", "https://edge.forgecdn.net.example.invalid/test.jar" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://edge.forgecdn.net/test.jar");
            CurseForge.AuthenticateDownload(request, "kiln-test-key");
            request.RequestUri = new Uri(target); CurseForge.AuthenticateDownload(request, "kiln-test-key");
            Check("Do not forward API key: " + new Uri(target).Host, !request.Headers.Contains("x-api-key"));
        }
        var oldLanguage = Localizer.Shared.Language;
        try
        {
            Localizer.Shared.Language = AppLanguage.ZhHant; Check("Traditional Chinese", Localization.T("启动游戏", "Play") == "啟動遊戲");
            Localizer.Shared.Language = AppLanguage.En; Check("English", Localization.T("启动游戏", "Play") == "Play");
        }
        finally { Localizer.Shared.Language = oldLanguage; }
        Check("Maven classifier", Maven.Path("org.lwjgl:lwjgl:3.3.1:natives-windows") == "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar");
        Check("Maven extension", Maven.Path("a.b:c:1@zip") == "a/b/c/1/c-1.zip");
        Check("Windows disallow rule", !new RuleContext("x86_64").Check(Json.Parse("[{\"action\":\"allow\"},{\"action\":\"disallow\",\"os\":{\"name\":\"windows\"}}]")));
        Check("macOS rule excluded", !new RuleContext("x86_64").Check(Json.Parse("[{\"action\":\"allow\",\"os\":{\"name\":\"osx\"}}]")));
        var architecture = Json.Parse("[{\"action\":\"allow\",\"os\":{\"name\":\"windows\",\"arch\":\"x86\"}}]");
        Check("Architecture rule", !new RuleContext("x86_64").Check(architecture) && new RuleContext("x86").Check(architecture));
        var demo = Json.Parse("[{\"action\":\"allow\",\"features\":{\"is_demo_user\":true}}]");
        Check("Demo feature", !new RuleContext("x86_64").Check(demo) && new RuleContext("x86_64", true).Check(demo));
        Check("Arguments with spaces and empty value", Launcher.Split("-Xmx2G \"-Dfoo=a b\" ''").SequenceEqual(new[] { "-Xmx2G", "-Dfoo=a b", "" }));
        Check("NeoForge 21.1", ForgeInstaller.NeoForgeGameVersion("21.1.77") == "1.21.1");
        Check("NeoForge 20.4", ForgeInstaller.NeoForgeGameVersion("20.4.237") == "1.20.4");
        Check("NeoForge 21.0", ForgeInstaller.NeoForgeGameVersion("21.0.167") == "1.21");
        Check("NeoForge 26.3", ForgeInstaller.NeoForgeGameVersion("26.3.0.65-beta") == "26.3");
        var parser = new Log4jParser();
        const string xml = "<log4j:Event logger=\"x\" timestamp=\"0\" level=\"WARN\" thread=\"Render thread\"><log4j:Message><![CDATA[Hello]]></log4j:Message></log4j:Event>";
        var lines = parser.Feed(xml[..40]).Concat(parser.Feed(xml[40..] + "\nplain line\n")).ToList();
        Check("Fragmented log4j XML", lines.Count == 2 && lines[0].EndsWith("[Render thread/WARN]: Hello") && lines[1] == "plain line");
        var processLog = new GameProcess("Parity"); processLog.Append(string.Join("\n", Enumerable.Range(0, 5001)) + "\n");
        var revision = processLog.LineRevision; var oldSnapshot = processLog.Lines;
        processLog.Append("latest-line\n");
        Check("Log keeps updating at 5000-line capacity", processLog.Lines.Count == 5000 && processLog.LineRevision > revision && processLog.Lines[^1] == "latest-line");
        Check("Log snapshot remains stable across writes", oldSnapshot.Count == 5000 && oldSnapshot[^1] == "5000");
        Check("Crash analysis", CrashAnalyzer.Analyze(new[] { "java.lang.UnsupportedClassVersionError: foo" }) != null);
        Check("Semantic version ordering", Updater.IsNewer("0.2.10", "0.2.9") && !Updater.IsNewer("0.2.1", "0.2.1") && Updater.IsNewer("1.0", "0.9.9"));
        var token = "test-token-0123456789";
        Check("Diagnostics redact token", !Diagnostics.Scrub("--accessToken " + token, token).Contains(token));
        var oldAccount = JsonSerializer.Deserialize<Account>("{\"kind\":\"offline\",\"name\":\"Steve\",\"uuid\":\"abc\"}");
        Check("Old account JSON", oldAccount?.Name == "Steve" && oldAccount.NeedsLogin == null);
        var oldSetup = JsonSerializer.Deserialize<LauncherSettings.InstanceSetup>("{\"icon\":\"stone\"}");
        Check("Old instance settings", oldSetup?.Icon == "stone" && oldSetup.Account == null);
        var root = Path.Combine(Path.GetTempPath(), "kiln-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a = Path.Combine(root, "versions", "ParityA"); Directory.CreateDirectory(Path.Combine(a, "ParityA-natives"));
            File.WriteAllText(Path.Combine(a, "ParityA.json"), "{\"id\":\"ParityA\",\"mainClass\":\"x\",\"type\":\"release\",\"releaseTime\":\"2024-01-01T00:00:00Z\",\"downloads\":{\"client\":{\"url\":\"https://example.invalid/client.jar\"}}}");
            File.WriteAllText(Path.Combine(a, "ParityA.jar"), "jar");
            var child = Path.Combine(root, "versions", "Child"); Directory.CreateDirectory(child); File.WriteAllText(Path.Combine(child, "Child.json"), "{\"id\":\"Child\",\"inheritsFrom\":\"ParityA\"}");
            var instance = new McInstance(root, "ParityA");
            Throws("Refuse rename of a dependency", () => InstanceManager.Rename(instance, "ParityB"));
            Directory.Delete(child, true);
            InstanceManager.Duplicate(instance, "ParityA2");
            var duplicated = Path.Combine(root, "versions", "ParityA2");
            Check("Duplicate JSON and JAR renamed; natives removed", Json.Parse(File.ReadAllText(Path.Combine(duplicated, "ParityA2.json")))["id"].String == "ParityA2" && File.Exists(Path.Combine(duplicated, "ParityA2.jar")) && !Directory.Exists(Path.Combine(duplicated, "ParityA-natives")));
            InstanceManager.Rename(instance, "ParityB");
            Check("Rename instance", !Directory.Exists(a) && File.Exists(Path.Combine(root, "versions", "ParityB", "ParityB.json")));
            Check("Name validation", InstanceManager.ValidateName("ParityB", root) != null && InstanceManager.ValidateName("a/b", root) != null && InstanceManager.ValidateName("New", root) == null);
            Check("Windows reserved filenames", InstanceManager.ValidateName("CON", root) != null && InstanceManager.ValidateName("NUL", root) != null && InstanceManager.ValidateName("name.", root) != null);
            Check("Modpack traversal blocked", Modpack.SafePath("../x", root) == null && Modpack.SafePath("/etc/x", root) == null && Modpack.SafePath("C:\\x", root) == null);
            Check("Modpack relative path", string.Equals(Modpack.SafePath("mods/a.jar", root), Path.Combine(root, "mods", "a.jar"), StringComparison.OrdinalIgnoreCase));
            var versions = new[] { "1.0", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9", "1.10", "1.11", "1.12", "1.13", "1.14", "1.15", "1.16", "1.17", "1.18", "1.19", "1.20", "1.21", "1.21.4", "1.21.5", "1.21.6", "1.21.9", "1.21.11", "26.1", "26.2", "26.3", "26.4" };
            foreach (var version in versions)
            {
                var scene = DotScenes.SceneFor(version); var buffer = new DotBuffer(250, 88); scene.PaintStatic(buffer); scene.PaintDynamic(buffer, 1.25, new SceneCast());
                Check("Scene " + version, DotRenderer.Render(buffer, 2, scene.Background).Length == 500 * 176 * 4);
            }
        }
        catch (Exception e) { Check("Fixture exception: " + e, false); }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"Parity: {passed} passed, {failed} failed. No PCL2 migration was performed.");
        return Task.FromResult(failed == 0 ? 0 : 1);
    }
}
