using Kiln.Core;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;

internal static class ActivationTests
{
    internal static void Run(Action<string, bool> check)
    {
        var name = "含 空格 + & ? # % 版本"; var world = "My world + 家 & ?"; var server = "[::1]:25565";
        var link = LaunchLink.Create(name, world, server);
        var parsed = LaunchLink.Parse(link);
        check("Launch link round-trips Unicode and reserved characters", parsed == new LaunchLink(LauncherAction.Launch, name, world, server));
        check("Launch link preserves world and server options", parsed?.Options() is { World: var w, ServerIp: var s } && w == world && s == server);
        check("Launch link supports original play alias", LaunchLink.Parse("KiLn://PLAY/Example?world=Home") == new LaunchLink(LauncherAction.Launch, "Example", "Home"));
        check("Empty launch link uses the selected instance", LaunchLink.Parse("kiln://launch") is { Action: LauncherAction.Launch, Instance: null });
        check("Launch query supports instance fallback", LaunchLink.Parse("kiln://play?instance=Example")?.Instance == "Example");
        check("Launch path takes precedence over instance query", LaunchLink.Parse("kiln://launch/Path?instance=Query")?.Instance == "Path");
        check("First repeated query parameter wins", LaunchLink.Parse("kiln://launch/Example?world=First&world=Second")?.World == "First");
        check("URI plus characters are literal, not form spaces", LaunchLink.Parse("kiln://launch/A+B?world=C+D") is { Instance: "A+B", World: "C+D" });
        check("Empty launch options are absent", LaunchLink.Parse("kiln://launch/Example?world=&server=") is { World: null, Server: null });
        check("Unknown schemes and actions are rejected", LaunchLink.Parse("https://launch/Example") == null && LaunchLink.Parse("kiln://invalid/Example") == null && LaunchLink.Parse("not a URL") == null);
        check("Oversized launch links are rejected", LaunchLink.Parse("kiln://launch/" + new string('a', 8192)) == null);
        check("Notification actions share the activation parser", LaunchLink.Parse("kiln://tasks")?.Action == LauncherAction.Tasks && LaunchLink.Parse("kiln://log")?.Action == LauncherAction.Log && LaunchLink.Parse("kiln://show")?.Action == LauncherAction.Show);
        var fixtureA = new McInstance(Path.GetTempPath(), "Activation A"); var fixtureB = new McInstance(Path.GetTempPath(), "Activation B");
        check("Launch resolution selects a named instance ignoring case", new LaunchLink(LauncherAction.Launch, "activation a").Resolve(new[] { fixtureA, fixtureB }, fixtureB.Name) == fixtureA);
        check("Launch resolution falls back to selected instance", new LaunchLink(LauncherAction.Launch).Resolve(new[] { fixtureA, fixtureB }, fixtureB.Name) == fixtureB);
        try { new LaunchLink(LauncherAction.Launch, "Missing").Resolve(new[] { fixtureA }, fixtureA.Name); check("Missing launch instance reports an error", false); }
        catch (LauncherException) { check("Missing launch instance reports an error", true); }

        var channelName = "Kiln.Activation.Test." + Guid.NewGuid().ToString("N");
        var received = new ConcurrentQueue<string>();
        using var owner = new InstanceChannel(channelName);
        check("Activation channel acquires ownership once", owner.TryOwn() && owner.TryOwn());
        check("Another thread cannot own the active instance", !Task.Run(() => { using var contender = new InstanceChannel(channelName); return contender.TryOwn(); }).GetAwaiter().GetResult());
        owner.Listen(received.Enqueue);
        var serverPid = 0;
        InstanceChannel.Send(channelName, link, beforeSend: pid => serverPid = pid).GetAwaiter().GetResult();
        check("Activation IPC acknowledges exact Unicode request and server PID", received.TryDequeue(out var got) && got == link && serverPid == Environment.ProcessId);
        InstanceChannel.Send(channelName, "").GetAwaiter().GetResult();
        check("Opening the app again forwards a focus request", received.TryDequeue(out got) && got == "");
        try { InstanceChannel.Send(channelName, "https://invalid/").GetAwaiter().GetResult(); check("Activation IPC rejects invalid messages", false); }
        catch (ArgumentException) { check("Activation IPC rejects invalid messages", received.IsEmpty); }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { InstanceChannel.Send(channelName, link, cancelled.Token).GetAwaiter().GetResult(); check("Activation IPC honors cancellation", false); }
            catch (OperationCanceledException) { check("Activation IPC honors cancellation", received.IsEmpty); }
        }
        // An incomplete client may not monopolize the only pipe instance.
        using (var stalled = new NamedPipeClientStream(".", channelName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            stalled.Connect(3000); var hello = new byte[4]; stalled.ReadExactly(hello);
            InstanceChannel.Send(channelName, "kiln://tasks").GetAwaiter().GetResult();
            check("Activation IPC recovers from a stalled client", received.TryDequeue(out got) && got == "kiln://tasks");
        }
        using (var oversized = new NamedPipeClientStream(".", channelName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            oversized.Connect(3000); var header = new byte[4]; oversized.ReadExactly(header);
            BinaryPrimitives.WriteInt32LittleEndian(header, 32769); oversized.Write(header);
            check("Activation IPC rejects oversized frames", oversized.ReadByte() == -1);
        }
        Task.WhenAll(Enumerable.Range(0, 12).Select(i => InstanceChannel.Send(channelName, LaunchLink.Create("Instance " + i)))).GetAwaiter().GetResult();
        check("Concurrent activations are delivered without dropped requests", received.Count == 12 && received.Distinct().Count() == 12);
        var completion = owner.Completion!; owner.Dispose();
        check("Activation listener stops cleanly", completion.Wait(TimeSpan.FromSeconds(3)) && completion.IsCompletedSuccessfully);
        using var successor = new InstanceChannel(channelName);
        check("Activation ownership is released after close", successor.TryOwn());
    }
}
