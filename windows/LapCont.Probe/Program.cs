// LapCont — Probe — Hardware checks without recording or remote control
// License: MIT
using System.Text.Json;
using LapCont.Platform;
using Vortice.MediaFoundation;
using System.IO.Pipes;
using System.Security.Principal;
using LapCont.Protocol;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
try
{
    object report = args.FirstOrDefault() switch
    {
        "ble" => await BleProbe.RunAsync(TimeSpan.FromSeconds(8), timeout.Token),
        "pipe" => await PipeProbeAsync(timeout.Token),
        "media-inventory" => MediaProbe.Inventory(),
        "camera" when args.Contains("--consent-camera") => await MediaProbe.CaptureAsync(timeout.Token),
        "opus" => AudioProbe.Synthetic(),
        "microphone" when args.Contains("--consent-microphone") => await AudioProbe.MicrophoneAsync(timeout.Token),
        _ => new { phase = 0, session_id = WindowsSessions.CurrentSessionId, active_console = WindowsSessions.WTSGetActiveConsoleSessionId(),
            user_query_available = WindowsSessions.User(WindowsSessions.CurrentSessionId) is not null, lock_state = "unknown" }
    };
    Console.WriteLine(JsonSerializer.Serialize(report));
}
catch (OperationCanceledException) { Console.WriteLine("{\"status\":\"PROBE_TIMEOUT\"}"); Environment.ExitCode = 1; }
catch (System.Runtime.InteropServices.COMException e) { Console.WriteLine(JsonSerializer.Serialize(new { status = "PLATFORM_FAILED", hresult = $"0x{e.HResult:X8}" })); Environment.ExitCode = 1; }
catch (UnauthorizedAccessException) { Console.WriteLine("{\"status\":\"PERMISSION_DENIED\"}"); Environment.ExitCode = 1; }
catch (SharpGen.Runtime.SharpGenException e) { Console.WriteLine(JsonSerializer.Serialize(new { status = "MEDIA_FOUNDATION_FAILED", hresult = $"0x{e.HResult:X8}" })); Environment.ExitCode = 1; }
catch (OpusSharp.Core.OpusException e) { Console.WriteLine(JsonSerializer.Serialize(new { status = "OPUS_FAILED", detail = e.Message })); Environment.ExitCode = 1; }

static async Task<object> PipeProbeAsync(CancellationToken ct)
{
    var session = WindowsSessions.CurrentSessionId; var sid = WindowsIdentity.GetCurrent().User!;
    using var server = SessionPipe.Create(session, sid);
    var connected = server.WaitForConnectionAsync(ct);
    using var client = new NamedPipeClientStream(".", $"LapCont.Session.{session}.Control", PipeDirection.InOut, PipeOptions.Asynchronous,
        TokenImpersonationLevel.Impersonation);
    await client.ConnectAsync(5000, ct); await connected;
    var verified = SessionPipe.VerifyClient(server, session, sid);
    var payload = System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"probe\":\"session_ipc\"}");
    await Frames.WriteAsync(client, payload, 4096, ct);
    var received = await Frames.ReadAsync(server, 4096, ct);
    return new { status = verified && received.SequenceEqual(payload) ? "same_session_pipe_identity_verified" : "IPC_FAILED",
        session_id = session, wrong_session_rejected = !SessionPipe.VerifyClient(server, session + 1, sid),
        wrong_sid_rejected = !SessionPipe.VerifyClient(server, session, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)),
        local_acl = "SYSTEM and intended SID; network SID denied", multi_user_integration = "unverified" };
}
