// LapCont — Security — Cumulative handshake and connection limits
// License: MIT
namespace LapCont.Security;

/// <summary>One reader/one writer accounting wrapper. Handshake traffic is capped before allocating application frames.</summary>
public sealed class TunnelBudget(Stream inner) : Stream
{
    private long total; private long limit = 262144;
    public void Authenticated() => Interlocked.Exchange(ref limit, 1L << 30);
    private void Account(int count) { if (Interlocked.Add(ref total, count) > Interlocked.Read(ref limit)) throw new IOException("Secure channel byte budget exhausted"); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { var count = await inner.ReadAsync(buffer, ct); Account(count); return count; }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Account(buffer.Length); await inner.WriteAsync(buffer, ct); }
    public override int Read(byte[] b, int o, int c) { var n = inner.Read(b, o, c); Account(n); return n; }
    public override void Write(byte[] b, int o, int c) { Account(c); inner.Write(b, o, c); }
    public override bool CanRead => true; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush(); public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
