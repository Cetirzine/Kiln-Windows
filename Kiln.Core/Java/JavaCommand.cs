using System.Diagnostics;
using System.Text;

namespace Kiln.Core;

// Drain both pipes concurrently; a broken runtime must not hang Java selection or launch.
internal static class JavaCommand
{
    internal sealed record Result(int ExitCode, string Output);
    internal static async Task<Result> Run(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Java could not be started.");
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stdout = Tail(process.StandardOutput, reads.Token); var stderr = Tail(process.StandardError, reads.Token);
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            var output = await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            return new(process.ExitCode, string.Join("\n", output));
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            reads.Cancel();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            throw;
        }
    }
    private static async Task<string> Tail(StreamReader reader, CancellationToken cancellationToken)
    {
        const int limit = 128 * 1024;
        var result = new StringBuilder(); var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) is var count && count > 0)
        {
            result.Append(buffer, 0, count);
            if (result.Length > limit) result.Remove(0, result.Length - limit);
        }
        return result.ToString();
    }
}
