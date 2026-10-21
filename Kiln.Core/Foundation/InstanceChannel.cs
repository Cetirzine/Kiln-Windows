using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;

namespace Kiln.Core;

/// Per-user, per-session activation. Mutex ownership stays on the caller's UI thread.
public sealed class InstanceChannel : IDisposable
{
    private const int MaxBytes = 32768;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _name;
    private int _ownerThread;
    private bool _disposed;
    public Task? Completion { get; private set; }

    public InstanceChannel(string name)
    {
        if (name.Length is 0 or > 180 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_')) throw new ArgumentException("Invalid activation channel name.", nameof(name));
        _name = name; _mutex = new Mutex(false, @"Local\" + name);
    }

    public bool TryOwn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownerThread != 0) return _ownerThread == Environment.CurrentManagedThreadId;
        bool owned;
        try { owned = _mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (owned) _ownerThread = Environment.CurrentManagedThreadId;
        return owned;
    }

    public void Listen(Action<string> receive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownerThread != Environment.CurrentManagedThreadId || Completion != null) throw new InvalidOperationException("Activation channel is not owned by this thread.");
        var stop = _stop.Token;
        Completion = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                    // An incomplete local client must not block future activations indefinitely.
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(stop); request.CancelAfter(TimeSpan.FromSeconds(2));
                    var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, Environment.ProcessId);
                    await pipe.WriteAsync(header, request.Token).ConfigureAwait(false);
                    await pipe.ReadExactlyAsync(header, request.Token).ConfigureAwait(false);
                    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                    if (length is < 0 or > MaxBytes) continue;
                    var bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, request.Token).ConfigureAwait(false);
                    var message = Utf8.GetString(bytes);
                    var valid = message.Length == 0 || LaunchLink.Parse(message) != null;
                    if (valid) receive(message);
                    await pipe.WriteAsync(new byte[] { valid ? (byte)1 : (byte)0 }, request.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (OperationCanceledException) { /* A stalled client timed out; accept the next one. */ }
                catch (Exception e)
                {
                    if (stop.IsCancellationRequested) break;
                    Log.log("[Activation] " + e.Friendly());
                    try { await Task.Delay(100, stop).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                }
            }
        });
    }

    public static async Task Send(string name, string message, CancellationToken ct = default, Action<int>? beforeSend = null)
    {
        var bytes = Utf8.GetBytes(message); if (bytes.Length > MaxBytes) throw new ArgumentException("Activation link is too long.", nameof(message));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        var header = new byte[4]; await pipe.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
        beforeSend?.Invoke(BinaryPrimitives.ReadInt32LittleEndian(header));
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, deadline.Token).ConfigureAwait(false); await pipe.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
        var ack = new byte[1]; await pipe.ReadExactlyAsync(ack, deadline.Token).ConfigureAwait(false);
        if (ack[0] != 1) throw new ArgumentException("Activation link was rejected.", nameof(message));
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_ownerThread != 0 && _ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Release activation ownership on the owning thread.");
        _disposed = true; _stop.Cancel();
        if (_ownerThread != 0) { _mutex.ReleaseMutex(); _ownerThread = 0; }
        _mutex.Dispose();
        if (Completion == null) _stop.Dispose();
        else _ = Completion.ContinueWith(_ => _stop.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
