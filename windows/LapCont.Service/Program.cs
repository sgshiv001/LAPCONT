// LapCont — Service — Explicit service or inventory entry point
// License: MIT
using System.ServiceProcess;
using System.Text.Json;
using LapCont.Platform;
using LapCont.Service;

if (args.Contains("--console"))
{
    var dataIndex = Array.IndexOf(args, "--data-directory");
    await using var host = new ProductHost(true, dataIndex >= 0 ? args[dataIndex + 1] : null);
    using var lifetime = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
    await host.StartAsync(); Console.WriteLine("LapCont coordinator development mode. Open LapCont.Session --development.");
    try { await Task.Delay(Timeout.Infinite, lifetime.Token); } catch (OperationCanceledException) { }
}
else if (args.Contains("--product-service")) ServiceBase.Run(new ProductService());
else if (args.Contains("--service"))
{
    var journalArgument = Array.IndexOf(args, "--journal-directory");
    if (journalArgument >= 0 && journalArgument + 1 >= args.Length) throw new ArgumentException("Missing journal directory");
    ServiceBase.Run(new SessionEventService(journalArgument >= 0 ? args[journalArgument + 1] : null));
}
else Console.WriteLine(JsonSerializer.Serialize(new
{
    phase = 0, mode = "inventory", current_session_id = WindowsSessions.CurrentSessionId,
    active_console_session_id = WindowsSessions.WTSGetActiveConsoleSessionId(),
    user_query_available = WindowsSessions.User(WindowsSessions.CurrentSessionId) is not null,
    lock_state = "unknown", scm_callbacks = "unverified_until_installed_and_tested"
}));
