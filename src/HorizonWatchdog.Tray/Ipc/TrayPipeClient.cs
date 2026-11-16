using System.IO.Pipes;
using System.Text.Json;
using HorizonControl.Shared.Ipc;

namespace HorizonWatchdog.Tray.Ipc;

public sealed class TrayPipeClient : IAsyncDisposable
{
    private NamedPipeClientStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    public event Func<IpcEnvelope, Task>? MessageReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _stream = new NamedPipeClientStream(".", IpcDefaults.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _stream.ConnectAsync(5000, cancellationToken);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public async Task SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_stream is null || !_stream.IsConnected)
        {
            throw new InvalidOperationException("Pipe not connected.");
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var len = BitConverter.GetBytes(json.Length);
        await _stream.WriteAsync(len, cancellationToken);
        await _stream.WriteAsync(json, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (_stream is { IsConnected: true } && !cancellationToken.IsCancellationRequested)
        {
            var envelope = await ReadEnvelopeAsync(_stream, cancellationToken);
            if (envelope is null)
            {
                break;
            }

            if (MessageReceived is not null)
            {
                await MessageReceived(envelope);
            }
        }
    }

    private static async Task<IpcEnvelope?> ReadEnvelopeAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(stream, lenBuf, cancellationToken))
        {
            return null;
        }

        var len = BitConverter.ToInt32(lenBuf);
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

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }
    }
}
