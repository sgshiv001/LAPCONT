// LapCont — Session — Desktop-only command execution with visible local Stop and independent leases
// License: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using LapCont.Core;
using LapCont.Platform;
using LapCont.Protocol;

namespace LapCont.Session;

/// <summary>Worker-owned companion runtime. No desktop capture before an authorized service call; local Stop has priority.</summary>
public sealed class SessionRuntime(bool development) : IAsyncDisposable
{
    private readonly CancellationTokenSource life = new();
    private readonly Channel<byte[]> control = Channel.CreateBounded<byte[]>(64);
    private readonly Channel<byte[]> media = Channel.CreateBounded<byte[]>(32);
    private int queuedMediaBytes;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim operations = new(1);
    private readonly object playbackGate = new();
    private CancellationTokenSource? streamLife, observerLife;
    private Task? capture, observer;
    private TalkPlayer? player;
    private string? streamId, talkId;
    private long streamRenewed, talkRenewed;
    private Task? worker;
    private CancellationTokenSource? connection;
    public event Action<string>? Activity;
    public event Action<JsonElement>? Notification;
    public void Start() => worker = Task.Run(RunAsync);
    public async Task<JsonElement> LocalAsync(string operation, object parameters)
    {
        if (connection is null || connection.IsCancellationRequested) throw new IOException("PC service is not connected");
        var id = Guid.NewGuid().ToString("N"); var promise = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = promise;
        try { Send(new { kind = "local_call", request_id = id, operation, @params = parameters }); var result = await promise.Task.WaitAsync(TimeSpan.FromSeconds(10), life.Token);
            if (!result.GetProperty("ok").GetBoolean()) throw new CommandFailure(result.GetProperty("code").GetString()!, "Local operation was rejected"); return result; }
        finally { pending.TryRemove(id, out _); }
    }
    private void Send(object value) { if (!control.Writer.TryWrite(Wire.Encode(value))) connection?.Cancel(); }
    public async Task StopAsync()
    {
        await operations.WaitAsync(); try { await StopCaptureAsync(); StopTalk(); Activity?.Invoke("Camera, microphone and talk-back stopped locally"); } finally { operations.Release(); }
        try { await LocalAsync("local_stop", new { }); } catch (IOException) { } catch (TimeoutException) { }
    }
    private async Task RunAsync()
    {
        while (!life.IsCancellationRequested)
        {
            using var connected = CancellationTokenSource.CreateLinkedTokenSource(life.Token); connection = connected;
            try
            {
                using var pipe = await CoordinatorPipe.ConnectAsync(WindowsSessions.CurrentSessionId, "Control", development, connected.Token);
                VerifyHello(await Frames.ReadAsync(pipe, Frames.ControlLimit, connected.Token));
                Activity?.Invoke("Connected to PC service · no camera or microphone active");
                var writer = Task.Run(async () => { await foreach (var b in control.Reader.ReadAllAsync(connected.Token)) await Frames.WriteAsync(pipe, b, Frames.ControlLimit, connected.Token); });
                var mediaTask = MediaAsync(connected.Token); var lease = LeaseAsync(connected.Token);
                var calls = Channel.CreateBounded<JsonElement>(32);
                var dispatch = Task.Run(async () => { await foreach (var call in calls.Reader.ReadAllAsync(connected.Token)) await HandleAsync(call, connected.Token); });
                var reader = Task.Run(async () =>
                {
                    while (!connected.IsCancellationRequested)
                    {
                        using var doc = Frames.ParseControl(await Frames.ReadAsync(pipe, Frames.ControlLimit, connected.Token)); var value = doc.RootElement; var kind = value.GetProperty("kind").GetString();
                        if (kind == "local_result") { if (pending.TryGetValue(value.GetProperty("request_id").GetString()!, out var p)) p.TrySetResult(value.GetProperty("result").Clone()); }
                        else if (kind == "call") { if (!calls.Writer.TryWrite(value.Clone())) throw new IOException("Control operation queue full"); }
                        else if (kind == "observer_config") await ConfigureObserverAsync(value, connected.Token);
                        else Notification?.Invoke(value.Clone());
                    }
                });
                var jobs = new[] { writer, reader, mediaTask, lease, dispatch };
                try { await await Task.WhenAny(jobs); }
                finally { connected.Cancel(); foreach (var job in jobs) try { await job; } catch (OperationCanceledException) { } catch (IOException) { } }
            }
            catch (OperationCanceledException) when (life.IsCancellationRequested) { }
            catch (Exception e) { Activity?.Invoke($"Service unavailable ({e.GetType().Name}). Open/install LapCont.Service."); }
            finally
            {
                connected.Cancel(); await operations.WaitAsync(); try { await StopCaptureAsync(); StopTalk(); } finally { operations.Release(); }
                observerLife?.Cancel(); if (observer is not null) try { await observer; } catch (OperationCanceledException) { }
                while (control.Reader.TryRead(out _)) { } DrainMedia();
                foreach (var p in pending.Values) p.TrySetException(new IOException("Service disconnected")); connection = null;
            }
            try { await Task.Delay(2000, life.Token); } catch (OperationCanceledException) { }
        }
    }
    private static void VerifyHello(byte[] bytes)
    {
        using var doc = Frames.ParseControl(bytes); var h = doc.RootElement;
        if (h.GetProperty("protocol").GetString() != "LPC1" || h.GetProperty("ipc_version").GetInt32() != 1 || h.GetProperty("windows_session_id").GetInt32() != WindowsSessions.CurrentSessionId || h.GetProperty("windows_sid").GetString() != WindowsSessions.CurrentSid)
            throw new UnauthorizedAccessException("Companion context mismatch");
    }
    private async Task MediaAsync(CancellationToken ct)
    {
        using var pipe = await CoordinatorPipe.ConnectAsync(WindowsSessions.CurrentSessionId, "Media", development, ct); VerifyHello(await Frames.ReadAsync(pipe, Frames.ControlLimit, ct));
        var writer = Task.Run(async () => { await foreach (var b in media.Reader.ReadAllAsync(ct)) try { await Frames.WriteAsync(pipe, b, Frames.MediaLimit + 40, ct); } finally { Interlocked.Add(ref queuedMediaBytes,-b.Length); } }, ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = Frames.DecodeMedia(await Frames.ReadAsync(pipe, Frames.MediaLimit + 40, ct));
                lock (playbackGate)
                {
                    if (frame.Type != 3) throw new InvalidDataException("Invalid talk media direction");
                    // A Stop can overtake already queued media on the separate pipe.
                    // Discard those ended-lease packets without playing or reconnecting.
                    if (Convert.ToHexString(frame.StreamId).ToLowerInvariant() != talkId || player is null) continue;
                    player.Packet(frame.Payload); talkRenewed = Stopwatch.GetTimestamp();
                }
            }
        }
        finally { connection?.Cancel(); try { await writer; } catch (OperationCanceledException) { } }
    }
    private async Task HandleAsync(JsonElement call, CancellationToken ct)
    {
        await operations.WaitAsync(ct);
        try
        {
            var op = call.GetProperty("operation").GetString(); var p = call.GetProperty("params");
            try
            {
                switch (op)
                {
                    case "lock": WindowsSessions.InitiateLocalLock(); Activity?.Invoke("Lock requested · waiting for Windows event"); break;
                    case "unlock_request": Activity?.Invoke("Your phone requested unlock. Sign in at your PC using Windows."); Notification?.Invoke(JsonSerializer.SerializeToElement(new { kind = "unlock_request" })); break;
                    case "stream_start": await StartCaptureAsync(p, ct); break;
                    case "stream_stop": await StopCaptureAsync(); break;
                    case "stream_renew": if (p.GetProperty("stream_id").GetString() != streamId) throw new InvalidOperationException("STREAM_NOT_ACTIVE"); streamRenewed = Stopwatch.GetTimestamp(); if (p.GetProperty("recovery").GetBoolean()) Interlocked.Exchange(ref keyframeRequested,1); break;
                    case "talk_start":
                        lock (playbackGate) { player = new TalkPlayer(p.TryGetProperty("speaker_device", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null); talkId = p.GetProperty("stream_id").GetString(); talkRenewed = Stopwatch.GetTimestamp(); }
                        Activity?.Invoke("PHONE TALK-BACK ACTIVE · local Stop available"); break;
                    case "talk_renew": if (p.GetProperty("stream_id").GetString() != talkId) throw new InvalidOperationException("TALK_NOT_ACTIVE"); talkRenewed = Stopwatch.GetTimestamp(); break;
                    case "talk_stop": StopTalk(); break;
                    default: throw new InvalidDataException("Unknown session operation");
                }
                Send(new { kind = "result", request_id = call.GetProperty("request_id").GetString(), ok = true, code = "OK" });
            }
            catch (Exception e)
            { Activity?.Invoke($"Operation failed ({e.GetType().Name})"); Send(new { kind = "result", request_id = call.GetProperty("request_id").GetString(), ok = false, code = "SESSION_OPERATION_FAILED" }); }
        }
        finally { operations.Release(); }
    }
    private int keyframeRequested;
    private async Task StartCaptureAsync(JsonElement p, CancellationToken ct)
    {
        await StopCaptureAsync(); Interlocked.Exchange(ref keyframeRequested,0); streamId = p.GetProperty("stream_id").GetString()!; var id = Convert.FromHexString(streamId);
        var settings = p.GetProperty("settings"); var video = settings.GetProperty("video").GetBoolean(); var audio = settings.GetProperty("audio").GetBoolean();
        var w = settings.GetProperty("width").GetInt32(); var h = settings.GetProperty("height").GetInt32(); var bitrate = settings.GetProperty("bitrate").GetInt32();
        streamLife = CancellationTokenSource.CreateLinkedTokenSource(ct); var token = streamLife.Token; streamRenewed = Stopwatch.GetTimestamp();
        var origin = Stopwatch.StartNew(); var sequence = 0UL; var sendGate = new object();
        var cameraAnnounced = 0;
        void Emit(byte type, byte[] payload, bool keyframe = false)
        {
            lock (sendGate)
            {
                var pts = origin.ElapsedTicks * 1000000 / Stopwatch.Frequency;
                var bytes=Frames.EncodeMedia(new(type, (ushort)(keyframe ? 1 : 0), id, sequence++, pts, payload));
                if(Interlocked.Add(ref queuedMediaBytes,bytes.Length)>4194304) {
                    Interlocked.Add(ref queuedMediaBytes,-bytes.Length); throw new IOException("Media receiver is too slow");
                }
                try {
                    if (!media.Writer.TryWrite(bytes)) {
                        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(250);
                        media.Writer.WriteAsync(bytes,deadline.Token).AsTask().GetAwaiter().GetResult();
                    }
                }
                catch(OperationCanceledException) when(!token.IsCancellationRequested) { Interlocked.Add(ref queuedMediaBytes,-bytes.Length); throw new IOException("Media receiver is too slow"); }
                catch { Interlocked.Add(ref queuedMediaBytes,-bytes.Length); throw; }
            }
        }
        void Metadata(string path) => Emit(5, Wire.Encode(new { width = w, height = h, fps = 30, bitrate, video, audio, sample_rate = 48000, channels = 1, opus_frame_samples = 960, encoder = path }));
        Metadata(video ? "Media Foundation negotiation pending" : "audio only");
        Activity?.Invoke($"LIVE {(video ? "CAMERA " : "")}{(audio ? "MICROPHONE " : "")}STARTING · Stop is always available");
        capture = Task.Run(async () =>
        {
            try
            {
                var tracks = new List<Task>();
                if (video) tracks.Add(LiveCamera.RunAsync(w, h, bitrate, (bytes, key) => { var config = H264.Configuration(bytes); if (config.Length > 0) Emit(4, config); Emit(1, bytes, key); if (Interlocked.Exchange(ref cameraAnnounced, 1) == 0) Activity?.Invoke("LIVE CAMERA ACTIVE · local Stop available"); }, token,Metadata,()=>Interlocked.Exchange(ref keyframeRequested,0)!=0));
                if (audio) tracks.Add(LiveAudio.CaptureAsync(bytes => Emit(2, bytes), token));
                var done = await Task.WhenAny(tracks); try { await done; } finally { streamLife?.Cancel(); }
                await Task.WhenAll(tracks);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e)
            {
                streamLife?.Cancel(); StopTalk(); Activity?.Invoke($"Capture stopped ({e.GetType().Name})");
                var boundary=e.Message switch { "Media receiver is too slow" => "MEDIA_BACKPRESSURE", "Camera ended" => "CAMERA_ENDED", _ => e.GetType().Name };
                Send(new { kind = "local_call", request_id = Guid.NewGuid().ToString("N"), operation = "capture_failed", @params = new { code = $"{boundary}:{e.HResult:X8}" } });
            }
        }, token);
    }
    private async Task StopCaptureAsync()
    {
        streamLife?.Cancel(); if (capture is not null) try { await capture; } catch (OperationCanceledException) { }
        streamLife?.Dispose(); streamLife = null; capture = null; streamId = null; DrainMedia();
        Activity?.Invoke("Camera and microphone stopped");
    }
    private void DrainMedia() { while(media.Reader.TryRead(out var bytes)) Interlocked.Add(ref queuedMediaBytes,-bytes.Length); }
    private void StopTalk() { lock (playbackGate) { player?.Dispose(); player = null; talkId = null; } Activity?.Invoke("Talk-back stopped"); }
    private async Task LeaseAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(250, ct); await operations.WaitAsync(ct);
            try { if (streamLife is not null && Stopwatch.GetElapsedTime(streamRenewed) > TimeSpan.FromSeconds(30)) await StopCaptureAsync();
                if (player is not null && Stopwatch.GetElapsedTime(talkRenewed) > TimeSpan.FromSeconds(5)) StopTalk(); }
            finally { operations.Release(); }
        }
    }
    private async Task ConfigureObserverAsync(JsonElement config, CancellationToken ct)
    {
        observerLife?.Cancel(); if (observer is not null) try { await observer; } catch (OperationCanceledException) { }
        observerLife?.Dispose(); observerLife = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (config.GetProperty("paused").GetBoolean()) return;
        var phones = config.GetProperty("phones").EnumerateArray().ToDictionary(p => p.GetProperty("phone_id").GetString()!, p => Convert.FromHexString(p.GetProperty("key").GetString()!));
        if (phones.Count == 0) return;
        // The reader owns/disposes the message document before this background worker runs.
        var pcId = config.GetProperty("pc_id").GetString()!;
        var token = observerLife.Token;
        observer = Task.Run(async () =>
        {
            try { await LiveObserver.RunAsync(pcId, phones,
                healthy => Send(new { kind = "local_call", request_id = Guid.NewGuid().ToString("N"), operation = "observer_health", @params = new { healthy } }),
                (id, rssi, observedAt) => Send(new { kind = "local_call", request_id = Guid.NewGuid().ToString("N"), operation = "rssi", @params = new { phone_id = id, rssi, observed_at = observedAt } }), token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Activity?.Invoke($"Bluetooth observer unavailable ({e.GetType().Name})"); Send(new { kind = "local_call", request_id = Guid.NewGuid().ToString("N"), operation = "observer_health", @params = new { healthy = false } }); }
        }, token);
    }
    public async ValueTask DisposeAsync() { life.Cancel(); connection?.Cancel(); if (worker is not null) await worker; life.Dispose(); operations.Dispose(); }
}
