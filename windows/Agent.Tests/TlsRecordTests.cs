// LapCont — Tests — Actual TLS record tamper and replay rejection
// License: MIT
using System.Net;
using System.Net.Sockets;
using LapCont.Protocol;
using LapCont.Security;
using Xunit;

namespace LapCont.Tests;

/// <summary>Tests modify ciphertext after successful authentication; no custom cryptography is substituted.</summary>
public sealed class TlsRecordTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MutatedOrDuplicatedRecordsAreRejected(bool duplicate)
    {
        using var pc = Identity.Create("pc"); using var phone = Identity.Create("phone");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var ct = new CancellationTokenSource(10000);
        var authenticated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync(ct.Token);
            await using var tls = await TlsTunnel.ServerAsync(tcp.GetStream(), pc, Identity.Fingerprint(phone), ct.Token);
            authenticated.SetResult();
            await Assert.ThrowsAnyAsync<IOException>(() => Frames.ReadAsync(tls, 4096, ct.Token));
        });
        using var client = new TcpClient(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, ct.Token);
        await using var transport = new RecordFaultStream(client.GetStream());
        await using var channel = await TlsTunnel.ClientAsync(transport, phone, Identity.Fingerprint(pc), ct.Token);
        await authenticated.Task.WaitAsync(ct.Token);
        transport.Fault = duplicate ? 2 : 1;
        try { await Frames.WriteAsync(channel, "sensitive_parameter"u8.ToArray(), 4096, ct.Token); }
        catch (IOException) { /* Server may reject/close before the sender's second write completes. */ }
        await server;
    }

    private sealed class RecordFaultStream(Stream underlying) : Stream
    {
        public int Fault { get; set; }
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => underlying.ReadAsync(b, ct);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default)
        {
            var fault = Fault; Fault = 0;
            var bytes = b.ToArray(); if (fault == 1) bytes[^1] ^= 1;
            await underlying.WriteAsync(bytes, ct); if (fault == 2) await underlying.WriteAsync(bytes, ct);
        }
        public override void Flush() => underlying.Flush();
        public override Task FlushAsync(CancellationToken ct) => underlying.FlushAsync(ct);
        public override int Read(byte[] b, int o, int n) => underlying.Read(b, o, n);
        public override void Write(byte[] b, int o, int n) => underlying.Write(b, o, n);
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        protected override void Dispose(bool d) { if (d) underlying.Dispose(); base.Dispose(d); }
    }
}
