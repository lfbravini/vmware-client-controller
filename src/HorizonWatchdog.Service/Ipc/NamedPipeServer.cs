using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using HorizonControl.Shared.Ipc;

namespace HorizonWatchdog.Service.Ipc;

public sealed class NamedPipeServer(ILogger<NamedPipeServer> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private NamedPipeServerStream? _active;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event Func<IpcEnvelope, CancellationToken, Task>? MessageReceived;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var stream = _active;
        if (stream is null || !stream.IsConnected)
        {
            throw new InvalidOperationException("Tray is not connected to the named pipe.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteEnvelopeAsync(stream, envelope, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var security = new PipeSecurity();
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    PipeAccessRights.ReadWrite,
                    AccessControlType.Allow));
                security.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));

                var server = NamedPipeServerStreamAcl.Create(
                    IpcDefaults.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0,
                    0,
                    security);

                await server.WaitForConnectionAsync(cancellationToken);
                _active = server;
                logger.LogInformation("Tray connected on named pipe");
                await ReadLoopAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Named pipe listener error; retrying");
                _active = null;
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task ReadLoopAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (stream.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var envelope = await ReadEnvelopeAsync(stream, cancellationToken);
                if (envelope is null)
                {
                    break;
                }

                if (MessageReceived is not null)
                {
                    await MessageReceived(envelope, cancellationToken);
                }
            }
        }
        finally
        {
            _active = null;
            await stream.DisposeAsync();
        }
    }

    public static async Task WriteEnvelopeAsync(Stream stream, IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope);
        if (json.Length > IpcDefaults.MaxFrameBytes)
        {
            throw new InvalidOperationException("IPC frame too large.");
        }

        var len = BitConverter.GetBytes(json.Length);
        await stream.WriteAsync(len, cancellationToken);
        await stream.WriteAsync(json, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<IpcEnvelope?> ReadEnvelopeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(stream, lenBuf, cancellationToken))
        {
            return null;
        }

        var len = BitConverter.ToInt32(lenBuf);
        if (len <= 0 || len > IpcDefaults.MaxFrameBytes)
        {
            throw new InvalidOperationException($"Invalid IPC frame length {len}.");
        }

        var payload = new byte[len];
        if (!await ReadExactAsync(stream, payload, cancellationToken))
        {
            return null;
        }

        return JsonSerializer.Deserialize<IpcEnvelope>(payload);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_listenTask is not null)
        {
            try { await _listenTask; } catch { /* ignore */ }
        }

        _writeLock.Dispose();
    }
}
