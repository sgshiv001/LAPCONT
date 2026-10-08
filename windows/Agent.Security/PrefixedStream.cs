// LapCont — Security — Already-read relay handshake prefix
// License: MIT
namespace LapCont.Security;
/// <summary>Single reader prepends one bounded message; subsequent bytes use normal carrier validation.</summary>
public sealed class PrefixedStream(Stream inner, byte[] prefix) : Stream
{
    private int at;
    public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) { if (at < prefix.Length) { var count = Math.Min(b.Length, prefix.Length - at); prefix.AsMemory(at, count).CopyTo(b); at += count; return count; } return await inner.ReadAsync(b, ct); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) => inner.WriteAsync(b, ct);
    public override int Read(byte[] b, int o, int c) { if (at < prefix.Length) { var count = Math.Min(c, prefix.Length - at); prefix.AsSpan(at, count).CopyTo(b.AsSpan(o)); at += count; return count; } return inner.Read(b, o, c); }
    public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush(); public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
