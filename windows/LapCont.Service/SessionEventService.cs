// LapCont — Service — Real SCM session callbacks with bounded asynchronous journal
// License: MIT
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Channels;
using LapCont.Platform;

namespace LapCont.Service;

/// <summary>Opt-in SCM probe. Callbacks enqueue only; one worker queries WTS identities and writes event metadata.</summary>
public sealed class SessionEventService : ServiceBase
{
    private readonly Channel<(SessionChangeReason Reason, int Session, DateTimeOffset When)> queue =
        Channel.CreateBounded<(SessionChangeReason, int, DateTimeOffset)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource lifetime = new();
    private Task? worker;
    private long dropped;
    private readonly string journalFolder;
    public SessionEventService(string? journalDirectory = null)
    {
        journalFolder = Path.GetFullPath(journalDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LapCont", "phase0"));
        ServiceName = "LapCont.Phase0"; CanHandleSessionChangeEvent = true; CanStop = true; AutoLog = true;
    }
    protected override void OnStart(string[] args) => worker = ProcessAsync(lifetime.Token);
    protected override void OnSessionChange(SessionChangeDescription change)
    {
        if (!queue.Writer.TryWrite((change.Reason, change.SessionId, DateTimeOffset.UtcNow))) Interlocked.Increment(ref dropped);
    }
    protected override void OnStop() { queue.Writer.TryComplete(); lifetime.Cancel(); }
    private async Task ProcessAsync(CancellationToken ct)
    {
        var folder = journalFolder;
        try
        {
            Directory.CreateDirectory(folder);
            await foreach (var value in queue.Reader.ReadAllAsync(ct))
            {
                // Usernames are intentionally omitted from the journal; record whether the real WTS query succeeded.
                var record = JsonSerializer.Serialize(new { event_id = Guid.NewGuid().ToString("N"),
                    observed_at_utc = value.When, windows_session_id = value.Session, reason = value.Reason.ToString(),
                    verified_user_available = WindowsSessions.User(value.Session) is not null, dropped_events = Interlocked.Read(ref dropped) });
                await File.AppendAllTextAsync(Path.Combine(folder, $"sessions-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"), record + Environment.NewLine, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (IOException e) { EventLog.WriteEntry($"Phase 0 event journal failed: {e.GetType().Name}", System.Diagnostics.EventLogEntryType.Error); }
        catch (UnauthorizedAccessException) { EventLog.WriteEntry("Phase 0 event journal access denied", System.Diagnostics.EventLogEntryType.Error); }
    }
    protected override void Dispose(bool disposing) { if (disposing) { lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}
