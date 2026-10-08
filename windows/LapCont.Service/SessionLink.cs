// LapCont — Service — Authenticated per-session control and separate media IPC
// License: MIT
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using LapCont.Core;
using LapCont.Platform;
using LapCont.Protocol;

namespace LapCont.Service;

/// <summary>One session connection. Bounded writes and correlated RPCs; media never uses the control queue.</summary>
public sealed class SessionLink(int session, string sid, Func<SessionLink, JsonElement, Task<object>> local,
    Func<SessionLink, MediaFrame, Task> media, Action<SessionLink, bool> availability, Action<string> diagnostic, CancellationToken parent) : IAsyncDisposable
{
    private readonly CancellationTokenSource life = CancellationTokenSource.CreateLinkedTokenSource(parent);
    private readonly Channel<byte[]> outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
    // Speech uses 20 ms packets and tolerates loss. Keep the newest bounded audio when
    // Wi-Fi delivers a burst; playback congestion must not tear down the control pipe.
    private readonly Channel<byte[]> playback = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Channel<JsonElement> localRequests = Channel.CreateBounded<JsonElement>(32);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    public int SessionId => session;
    public string Sid => sid;
    public bool Available { get; private set; }
    private Task? worker;
    public void Start() => worker = Task.WhenAll(ControlAsync(), MediaAsync());
    public void Notify(object value)
    {
        var bytes = Wire.Encode(value); if (bytes.Length > Frames.ControlLimit || !outgoing.Writer.TryWrite(bytes)) life.Cancel();
    }
    public void Play(MediaFrame value) { if (!playback.Writer.TryWrite(Frames.EncodeMedia(value))) life.Cancel(); }
    public async Task<JsonElement> CallAsync(string operation, object parameters, CancellationToken ct)
    {
        if (!Available) throw new CommandFailure("SESSION_AGENT_UNAVAILABLE", "Open the PC companion in this Windows session");
        var id = Guid.NewGuid().ToString("N"); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            Notify(new { kind = "call", request_id = id, operation, @params = parameters });
            var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
            if (!response.GetProperty("ok").GetBoolean()) throw new CommandFailure(response.GetProperty("code").GetString()!, "Session companion could not complete the operation");
            return response;
        }
        catch (TimeoutException) { throw new CommandFailure("SESSION_AGENT_TIMEOUT", "The PC companion did not respond"); }
        finally { pending.TryRemove(id, out _); }
    }
    private async Task ControlAsync()
    {
        try
        {
            using var pipe = CoordinatorPipe.Server(session, sid, "Control"); await pipe.WaitForConnectionAsync(life.Token);
            if (!CoordinatorPipe.VerifyUser(pipe, session, sid)) throw new UnauthorizedAccessException("IPC user mismatch");
            await Frames.WriteAsync(pipe, Wire.Encode(new { protocol = "LPC1", ipc_version = 1, windows_session_id = session, windows_sid = sid }), Frames.ControlLimit, life.Token);
            Available = true; availability(this, true);
            var writer = Task.Run(async () => { await foreach (var bytes in outgoing.Reader.ReadAllAsync(life.Token)) await Frames.WriteAsync(pipe, bytes, Frames.ControlLimit, life.Token); });
            var localWorker = Task.Run(async () =>
            {
                await foreach (var request in localRequests.Reader.ReadAllAsync(life.Token))
                {
                    object result;
                    try { result = await local(this, request); }
                    catch (CommandFailure e) { result = new { ok = false, code = e.Code }; }
                    catch (Exception e) { diagnostic($"local operation session={session} error={e.GetType().Name}"); result = new { ok = false, code = "LOCAL_OPERATION_FAILED" }; }
                    Notify(new { kind = "local_result", request_id = request.GetProperty("request_id").GetString(), result });
                }
            });
            try
            {
                while (!life.IsCancellationRequested)
                {
                    using var doc = Frames.ParseControl(await Frames.ReadAsync(pipe, Frames.ControlLimit, life.Token)); var value = doc.RootElement;
                    var kind = value.GetProperty("kind").GetString();
                    if (kind == "result") { var id = value.GetProperty("request_id").GetString()!; if (pending.TryGetValue(id, out var request)) request.TrySetResult(value.Clone()); }
                    else if (kind == "local_call")
                    {
                        if (!localRequests.Writer.TryWrite(value.Clone())) throw new IOException("Local control queue full");
                    }
                    else throw new InvalidDataException("Unknown IPC envelope");
                }
            }
            finally { life.Cancel(); try { await Task.WhenAll(writer, localWorker); } catch (OperationCanceledException) { } }
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested) { }
        catch (Exception e) { diagnostic($"IPC control session={session} error={e.GetType().Name}"); }
        finally { Available = false; availability(this, false); life.Cancel(); foreach (var p in pending.Values) p.TrySetException(new IOException("Companion disconnected")); }
    }
    private async Task MediaAsync()
    {
        try
        {
            using var pipe = CoordinatorPipe.Server(session, sid, "Media"); await pipe.WaitForConnectionAsync(life.Token);
            if (!CoordinatorPipe.VerifyUser(pipe, session, sid)) throw new UnauthorizedAccessException("IPC media user mismatch");
            await Frames.WriteAsync(pipe, Wire.Encode(new { protocol = "LPC1", ipc_version = 1, windows_session_id = session, windows_sid = sid }), Frames.ControlLimit, life.Token);
            var writer = Task.Run(async () => { await foreach (var bytes in playback.Reader.ReadAllAsync(life.Token)) await Frames.WriteAsync(pipe, bytes, Frames.MediaLimit + 40, life.Token); });
            try { while (!life.IsCancellationRequested) await media(this, Frames.DecodeMedia(await Frames.ReadAsync(pipe, Frames.MediaLimit + 40, life.Token))); }
            finally { life.Cancel(); try { await writer; } catch (OperationCanceledException) { } }
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested) { }
        catch (Exception e) { diagnostic($"IPC media session={session} error={e.GetType().Name}"); }
        finally { life.Cancel(); }
    }
    public async ValueTask DisposeAsync() { life.Cancel(); if (worker is not null) await worker; life.Dispose(); }
}
