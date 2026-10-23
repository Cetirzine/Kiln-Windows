using Kiln.Core;

internal static class GamePathAliasTests
{
    internal static void Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "kiln-path-test-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "游戏 [路径] O'Brien"); var home = Path.Combine(root, "aliases");
        try
        {
            check("Java 21 module paths with URI-encoded characters retain base CDS without dynamic dumping", !Launcher.DynamicCdsSupported(21, new[] { "-p", "modules" }, "C:/Game Folder/a.jar"));
            check("Classpath games and plain module paths retain dynamic CDS", Launcher.DynamicCdsSupported(21, Array.Empty<string>(), "C:/Game Folder/a.jar") && Launcher.DynamicCdsSupported(21, new[] { "--module-path=modules" }, "C:/Game/a.jar"));
            check("Java 25 AOT settings are unaffected by the Java 21 compatibility guard", Launcher.DynamicCdsSupported(25, new[] { "-p", "modules" }, "C:/Game Folder/a.jar"));
            var name = "Forge [profile]"; var dir = Path.Combine(folder, "versions", name); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".json"), """{"id":"fixture","mainClass":"fixture","libraries":[{"name":"net.minecraftforge:forge:1.21.1-52.1.20"}]}""");
            var instance = new McInstance(folder, name); Directory.CreateDirectory(instance.NativesDir);
            var marker = Path.Combine(folder, "keep.txt"); File.WriteAllText(marker, "Original game data");
            var map = GamePathAliases.Prepare(instance, home); var mapped = map.Apply(marker);
            check("Forge path aliases open the same original files through Windows junctions", map.Active && !mapped.Contains('[') && File.ReadAllText(mapped) == "Original game data");
            File.WriteAllText(mapped, "Same file");
            check("Path aliases do not copy or move game data", File.ReadAllText(marker) == "Same file");
            check("Isolated game and native directory names are mapped without brackets", !map.Apply(instance.GameDir).Contains('[') && !map.Apply(instance.NativesDir).Contains('['));
            check("Path replacement respects directory boundaries", map.Apply(folder + "-neighbour\\file.jar") == folder + "-neighbour\\file.jar");
            check("Persistent path aliases are reused consistently", GamePathAliases.Prepare(instance, home).Apply(marker) == mapped);
            var alias = Path.GetDirectoryName(mapped)!; var other = Path.Combine(root, "other"); Directory.CreateDirectory(other);
            var refused = false; try { GamePathAliases.EnsureJunction(alias, other); } catch (IOException) { refused = true; }
            check("Existing path aliases cannot be redirected to another target", refused && File.ReadAllText(marker) == "Same file");
            foreach (var link in Directory.EnumerateDirectories(home)) Directory.Delete(link);
            check("Removing a path alias leaves the original game directory intact", File.ReadAllText(marker) == "Same file" && Directory.Exists(dir));
        }
        finally
        {
            if (!Path.GetFileName(root).StartsWith("kiln-path-test-", StringComparison.Ordinal) || !JavaManager.IsUnder(root, Path.GetTempPath())) throw new InvalidOperationException("Invalid fixture root");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
