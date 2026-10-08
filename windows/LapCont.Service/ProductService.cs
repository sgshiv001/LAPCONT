// LapCont — Service — Prompt SCM callbacks with bounded asynchronous processing
// License: MIT
using System.ServiceProcess;
using System.Threading.Channels;

namespace LapCont.Service;

/// <summary>LocalSystem is required for WTSQueryUserToken and protected machine identity. Desktop operations stay in the companion.</summary>
public sealed class ProductService : ServiceBase
{
    private readonly CancellationTokenSource life = new();
    private readonly Channel<(int Id, string Reason)> events = Channel.CreateBounded<(int, string)>(128);
    private Task? worker;
    public ProductService() { ServiceName = "LapCont"; CanStop = true; CanHandleSessionChangeEvent = true; CanHandlePowerEvent = true; }
    protected override void OnStart(string[] args)
    {
        worker = Task.Run(async () =>
        {
            await using var host = new ProductHost(false); await host.StartAsync();
            await foreach (var e in events.Reader.ReadAllAsync(life.Token))
                try { await host.Coordinator.ObserveAsync(e.Id, e.Reason, life.Token); }
                catch (Exception error) { host.Coordinator.Log($"session event={e.Reason} error={error.GetType().Name}"); }
        });
        _ = worker.ContinueWith(t => { if (t.IsFaulted) { EventLog.WriteEntry("LapCont coordinator stopped after a startup/runtime failure.", System.Diagnostics.EventLogEntryType.Error); Stop(); } }, TaskScheduler.Default);
    }
    protected override void OnSessionChange(SessionChangeDescription change)
    { if (!events.Writer.TryWrite((change.SessionId, change.Reason.ToString()))) EventLog.WriteEntry("LapCont event queue overflow; status requires refresh.", System.Diagnostics.EventLogEntryType.Warning); }
    protected override bool OnPowerEvent(PowerBroadcastStatus status)
    { if (status is PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend or PowerBroadcastStatus.Suspend) events.Writer.TryWrite(((int)LapCont.Platform.WindowsSessions.WTSGetActiveConsoleSessionId(), "PowerTransition")); return true; }
    protected override void OnStop() { life.Cancel(); events.Writer.TryComplete(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            life.Cancel(); events.Writer.TryComplete();
            if (worker is null || worker.IsCompleted) life.Dispose();
            else _ = worker.ContinueWith(_ => life.Dispose(),TaskScheduler.Default);
        }
        base.Dispose(disposing);
    }
}
