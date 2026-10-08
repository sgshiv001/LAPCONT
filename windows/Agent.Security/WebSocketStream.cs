// LapCont — Security — Bounded opaque WebSocket-to-stream adapter
// License: MIT
using System.Net.WebSockets;

namespace LapCont.Security;

/// <summary>Duplex TLS carrier; exactly one reader and one writer permitted. Owns the WebSocket lifetime.
/// Use async I/O on worker threads. A five-second synchronous bridge supports platform TLS alert writes.</summary>
public sealed class WebSocketStream(WebSocket socket) : Stream
{
    private const int ChunkLimit = 65536;
    private int messageBytes;
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (buffer.Length == 0) return 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer[..Math.Min(buffer.Length, ChunkLimit)], ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return 0;
            if (result.MessageType != WebSocketMessageType.Binary || messageBytes + result.Count > ChunkLimit)
                throw new InvalidDataException("Invalid TLS carrier chunk");
            messageBytes = result.EndOfMessage ? 0 : messageBytes + result.Count;
            if (result.Count != 0) return result.Count;
        }
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        while (!bytes.IsEmpty)
        {
            var count = Math.Min(bytes.Length, ChunkLimit);
            await socket.SendAsync(bytes[..count], WebSocketMessageType.Binary, true, ct).ConfigureAwait(false); bytes = bytes[count..];
        }
    }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return ReadAsync(buffer.AsMemory(offset, count), deadline.Token).AsTask().GetAwaiter().GetResult();
    }
    public override int Read(Span<byte> buffer)
    {
        var bytes = new byte[Math.Min(buffer.Length, ChunkLimit)];
        var count = Read(bytes, 0, bytes.Length); bytes.AsSpan(0, count).CopyTo(buffer); return count;
    }
    public override void Write(byte[] buffer, int offset, int count)
    {
        // .NET 8 Schannel sends rejection alerts synchronously even from AuthenticateAsServerAsync.
        // The bridge is bounded and never captures a UI synchronization context.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WriteAsync(buffer.AsMemory(offset, count), deadline.Token).AsTask().GetAwaiter().GetResult();
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > ChunkLimit) throw new InvalidDataException("Synchronous TLS carrier write exceeds bound");
        var bytes = buffer.ToArray(); Write(bytes, 0, bytes.Length);
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) socket.Dispose(); base.Dispose(disposing); }
}
