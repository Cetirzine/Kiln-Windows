using System.Diagnostics;
using System.Text;

namespace Kiln.Core;

public static partial class Updater
{
    public static string ReplaceScript(string staged, string appDir, int pid, bool relaunch, IReadOnlyList<string>? arguments = null, string? logPath = null)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        ValidateSibling(staged, appDir); appDir = ApplicationDirectory(appDir); staged = ApplicationDirectory(staged);
        var parent = Path.GetDirectoryName(appDir)!; var id = Guid.NewGuid().ToString("N");
        var backup = Path.Combine(parent, ".Kiln-previous-" + id);
        logPath ??= Path.Combine(Paths.Logs, "update-" + id + ".log");
        var argumentText = arguments is { Count: > 0 } ? string.Join(" ", arguments.Select(QuoteArgument)) : "";
        var launch = relaunch ? $$"""
            $start = New-Object System.Diagnostics.ProcessStartInfo
            $start.FileName = {{Q(Path.Combine(appDir, ExeName))}}
            $start.WorkingDirectory = $app
            $start.Arguments = {{Q(argumentText)}}
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $launched = [Diagnostics.Process]::Start($start)
            """ : "";
        return $$"""
        # Kiln update transaction. Every filesystem target is an absolute, validated sibling.
        $ErrorActionPreference = 'Stop'
        $app = {{Q(appDir)}}
        $staged = {{Q(staged)}}
        $backup = {{Q(backup)}}
        $parent = {{Q(parent)}}
        $log = {{Q(Path.GetFullPath(logPath))}}
        function Note([string] $message) { try { Add-Content -LiteralPath $log -Encoding UTF8 -Value ((Get-Date -Format o) + ' ' + $message) } catch { } }
        function Owned([string] $path, [string] $prefix) {
            $full = [IO.Path]::GetFullPath($path).TrimEnd('\')
            if ([IO.Path]::GetDirectoryName($full) -ine $parent -or -not [IO.Path]::GetFileName($full).StartsWith($prefix, [StringComparison]::Ordinal)) { throw 'Unexpected update path.' }
        }
        function MoveRetry([string] $source, [string] $destination) {
            for ($attempt = 0; $attempt -lt 25; $attempt++) {
                try { if (Test-Path -LiteralPath $destination) { throw 'Update destination already exists.' }; [IO.Directory]::Move($source, $destination); return }
                catch { if ($attempt -eq 24) { throw }; Start-Sleep -Milliseconds 200 }
            }
        }
        function StartLauncher {
            {{launch}}
            if ($launched -and $launched.WaitForExit(3000) -and $launched.ExitCode -ne 0) { throw ('Launcher exited with code ' + $launched.ExitCode) }
        }
        $ok = $false
        $moved = $false
        $installed = $false
        try {
            if ([IO.Path]::GetDirectoryName($app) -ine $parent -or $app -ieq $staged) { throw 'Unexpected app directory.' }
            Owned $staged '.Kiln-update-'
            Owned $backup '.Kiln-previous-'
            if (Test-Path -LiteralPath $backup) { throw 'Backup already exists.' }
            $owner = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
            if ($owner -and -not $owner.WaitForExit(60000)) { throw 'Launcher did not exit within 60 seconds.' }
            if (-not (Test-Path -LiteralPath (Join-Path $staged '{{ExeName}}') -PathType Leaf)) { throw 'Prepared launcher is missing.' }
            MoveRetry $app $backup
            $moved = $true
            MoveRetry $staged $app
            $installed = $true
            StartLauncher
            $ok = $true
            Note 'Update installed successfully.'
        } catch {
            Note ('Update failed: ' + $_.Exception.Message)
            if ($moved) {
                try {
                    if ($installed -and (Test-Path -LiteralPath $app)) { MoveRetry $app $staged }
                    if (-not (Test-Path -LiteralPath $app)) { MoveRetry $backup $app; Note 'Previous launcher restored.' }
                } catch { Note ('Rollback failed; preserved backup: ' + $backup + ' - ' + $_.Exception.Message) }
            }
            if ((-not $moved -or -not (Test-Path -LiteralPath $backup)) -and (Test-Path -LiteralPath (Join-Path $app '{{ExeName}}') -PathType Leaf)) {
                try { StartLauncher } catch { Note ('Could not reopen previous launcher: ' + $_.Exception.Message) }
            }
        }
        if ($ok) {
            try { Owned $backup '.Kiln-previous-'; Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop }
            catch { Note ('Update succeeded; retained backup: ' + $backup) }
            exit 0
        }
        exit 1
        """;
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
            result.Append('\\', slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public static Process LaunchReplacement(string staged, string appDir, bool relaunch = true, IReadOnlyList<string>? arguments = null, string? logPath = null)
    {
        var script = Path.Combine(Paths.Cache, "kiln-update-" + Guid.NewGuid().ToString("N") + ".ps1");
        // Windows PowerShell 5.1 needs a BOM to interpret Chinese paths as UTF-8.
        File.WriteAllText(script, ReplaceScript(staged, appDir, Environment.ProcessId, relaunch, arguments, logPath), new UTF8Encoding(true));
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script }) start.ArgumentList.Add(argument);
        var p = Process.Start(start) ?? throw new IOException("Couldn't start the update helper.");
        Log.log($"[Update] 已启动替换脚本：{Path.GetFileName(staged)} → {appDir}"); return p;
    }
}
