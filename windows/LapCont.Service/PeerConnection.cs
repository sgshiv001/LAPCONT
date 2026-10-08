// LapCont — Service — Bounded duplex authenticated control/media session
// License: MIT
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using LapCont.Core;
using LapCont.Protocol;

namespace LapCont.Service;

/// <summary>One authenticated phone. Full queues close the session and stop leases instead of unbounded buffering.</summary>
public sealed class PeerConnection(string phone, string pin, CancellationToken parent) : IDisposable
{
    private readonly CancellationTokenSource life = CancellationTokenSource.CreateLinkedTokenSource(parent);
    private readonly Channel<byte[]> controlQueue = Channel.CreateBounded<byte[]>(64);
    private readonly Channel<byte[]> mediaQueue = Channel.CreateBounded<byte[]>(32);
    private int queuedMediaBytes;
    public string? CloseReason { get; private set; }
    public string PhoneId => phone;
    public string Pin => pin;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Attachment { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public bool Alive => !life.IsCancellationRequested;
    public SslStream? Media { get; set; }
    public void Send(byte[] bytes) { if (bytes.Length > Frames.ControlLimit || !controlQueue.Writer.TryWrite(bytes)) { CloseReason="CONTROL_BACKPRESSURE_OR_OVERSIZE"; Close(); } }
    public void SendMedia(byte[] bytes) {
        if(!Alive) return;
        if(Interlocked.Add(ref queuedMediaBytes,bytes.Length)>4194304 || !mediaQueue.Writer.TryWrite(bytes)) {
            Interlocked.Add(ref queuedMediaBytes,-bytes.Length); CloseReason="MEDIA_BACKPRESSURE"; Close();
        }
    }
    public void Close() { try { life.Cancel(); } catch (ObjectDisposedException) { } }
    public async Task ControlAsync(SslStream tls, Coordinator coordinator)
    {
        life.CancelAfter(TimeSpan.FromMinutes(10)); var ct = life.Token; long lastSequence = 0;
        var writer = Task.Run(async () => { try { await foreach (var bytes in controlQueue.Reader.ReadAllAsync(ct)) await Frames.WriteAsync(tls, bytes, Frames.ControlLimit, ct); } finally { Close(); } }, ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytes = await Frames.ReadAsync(tls, Frames.ControlLimit, ct); string id = new('0', 32);
                try
                {
                    using (var json = Frames.ParseControl(bytes))
                        if (json.RootElement.TryGetProperty("request_id", out var rid) && rid.ValueKind == JsonValueKind.String && Command.HexId(rid.GetString()!)) id = rid.GetString()!;
                    var command = Command.Parse(bytes, coordinator.PcId, PhoneId, Id, DateTimeOffset.UtcNow);
                    if (command.Sequence <= lastSequence) throw new CommandFailure("REPLAY", "Sequence already used"); lastSequence = command.Sequence;
                    Send(await coordinator.ExecuteAsync(this, command, ct));
                    if (command.Name == "unpair") { await Task.Delay(150, ct); return; }
                }
                catch (CommandFailure e) { Send(Coordinator.Response(id, "failed", e.Code, e.Message)); }
                catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or InvalidDataException)
                { Send(Coordinator.Response(id, "failed", "INVALID_REQUEST", "Malformed LPC1 request")); }
            }
        }
        finally { Close(); try { await writer; } catch (OperationCanceledException) { } }
    }
    public async Task MediaAsync(SslStream tls, Coordinator coordinator, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token); var token = linked.Token;
        var sequences = new Dictionary<string, ulong>();
        var writer = Task.Run(async () => { try { await foreach (var bytes in mediaQueue.Reader.ReadAllAsync(token)) try { await Frames.WriteAsync(tls, bytes, Frames.MediaLimit + 40, token); } finally { Interlocked.Add(ref queuedMediaBytes,-bytes.Length); } } finally { Close(); } }, token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var frame = Frames.DecodeMedia(await Frames.ReadAsync(tls, Frames.MediaLimit + 40, token)); var stream = Convert.ToHexString(frame.StreamId);
                if (sequences.TryGetValue(stream, out var prior) && frame.Sequence <= prior) throw new InvalidDataException("Replayed media");
                if (!sequences.ContainsKey(stream) && sequences.Count >= 32) throw new InvalidDataException("Too many streams");
                sequences[stream] = frame.Sequence; await coordinator.TalkFrameAsync(this, frame, token);
            }
        }
        finally { Close(); try { await writer; } catch (OperationCanceledException) { } }
    }
    public void Dispose() { Close(); life.Dispose(); }
}
