namespace Kiln.Core;

public enum LauncherAction { Launch, Tasks, Log, Show }

/// Shared by protocol activation, shortcuts and notification actions.
public sealed record LaunchLink(LauncherAction Action, string? Instance = null, string? World = null, string? Server = null)
{
    public static LaunchLink? Parse(string value)
    {
        if (value.Length > 8192 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("kiln", StringComparison.OrdinalIgnoreCase)) return null;
        if (uri.Host.Equals("tasks", StringComparison.OrdinalIgnoreCase)) return new(LauncherAction.Tasks);
        if (uri.Host.Equals("log", StringComparison.OrdinalIgnoreCase)) return new(LauncherAction.Log);
        if (uri.Host.Equals("show", StringComparison.OrdinalIgnoreCase)) return new(LauncherAction.Show);
        if (!uri.Host.Equals("launch", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("play", StringComparison.OrdinalIgnoreCase)) return null;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2);
            query.TryAdd(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        var name = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (name.Length == 0) name = query.GetValueOrDefault("instance") ?? "";
        static string? Nonempty(string? text) => string.IsNullOrEmpty(text) ? null : text;
        return new(LauncherAction.Launch, Nonempty(name), Nonempty(query.GetValueOrDefault("world")), Nonempty(query.GetValueOrDefault("server")));
    }

    public static string Create(string instance, string? world = null, string? server = null)
    {
        var values = new List<string>();
        if (!string.IsNullOrEmpty(world)) values.Add("world=" + Uri.EscapeDataString(world));
        if (!string.IsNullOrEmpty(server)) values.Add("server=" + Uri.EscapeDataString(server));
        return "kiln://launch/" + Uri.EscapeDataString(instance) + (values.Count == 0 ? "" : "?" + string.Join('&', values));
    }

    public McInstance Resolve(IReadOnlyList<McInstance> instances, string? selected)
    {
        var name = Instance ?? selected;
        return instances.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new LauncherException(Localization.T(name == null ? "请先选择一个版本。" : $"找不到版本 {name}", name == null ? "Select an instance first." : $"Instance {name} wasn't found"));
    }

    public LaunchOptions Options() => new() { World = World, ServerIp = Server };
}
