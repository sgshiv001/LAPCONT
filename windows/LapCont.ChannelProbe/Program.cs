// LapCont — Channel probe — Runtime-only fixtures and nested mutual TLS
// License: MIT
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LapCont.Protocol;
using LapCont.Security;

var fixtureFolder = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/channel-fixture");
if (Directory.Exists(fixtureFolder) && Directory.EnumerateFileSystemEntries(fixtureFolder).Any())
    throw new IOException("Select a new empty private fixture directory; existing files will not be overwritten.");
Directory.CreateDirectory(fixtureFolder);
if (OperatingSystem.IsWindows())
{
    var acl = new DirectorySecurity();
    acl.SetAccessRuleProtection(true, false);
    var sid = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("No interactive user SID");
    foreach (var principal in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
        acl.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
    new DirectoryInfo(fixtureFolder).SetAccessControl(acl);
}
else throw new PlatformNotSupportedException("This fixture host is a Windows Schannel probe.");
var ownedFiles = new List<string>();
try
{
using var pc = Identity.Create("LapCont-PC-Probe");
using var phone = Identity.Create("LapCont-Phone-Probe");
var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
// Temporary test credentials only. The production phone private key will stay in Android Keystore.
WritePrivateFixture("phone.p12", phone.Export(X509ContentType.Pfx, password));
var fixture = new { url = "wss://localhost:7443/control", pc_fingerprint = Identity.Fingerprint(pc),
    phone_fingerprint = Identity.Fingerprint(phone), password,
    client_identity = Path.Combine(fixtureFolder, "phone.p12"), pc_id = Identity.Fingerprint(pc), phone_id = Identity.Fingerprint(phone) };
WritePrivateFixture("fixture.json", JsonSerializer.SerializeToUtf8Bytes(fixture));
var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 7443, l => l.UseHttps(h =>
{ h.ServerCertificate = pc; h.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13; })));
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
var app = builder.Build();
app.UseWebSockets();
app.Map("/{channel}", async context =>
{
    var channel = context.Request.RouteValues["channel"]?.ToString();
    if (!context.WebSockets.IsWebSocketRequest || channel is not ("control" or "media")) { context.Response.StatusCode = 400; return; }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(15));
    try
    {
        await using var carrier = new WebSocketStream(socket);
        await using var tls = await TlsTunnel.ServerAsync(carrier, pc, Identity.Fingerprint(phone), timeout.Token);
        var hello = JsonSerializer.SerializeToUtf8Bytes(new { protocol = "LPC1", kind = "context", channel,
            pc_id = fixture.pc_id, phone_id = fixture.phone_id, connection_session_id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) });
        await Frames.WriteAsync(tls, hello, Frames.ControlLimit, timeout.Token);
        var payload = await Frames.ReadAsync(tls, channel == "media" ? Frames.MediaLimit + Frames.MediaHeaderBytes : Frames.ControlLimit, timeout.Token);
        if (channel == "control")
        {
            using var json = Frames.ParseControl(payload);
            if (json.RootElement.GetProperty("probe").GetString() != "CSharp-Kotlin-mTLS") throw new InvalidDataException("Unexpected probe payload");
        }
        else Frames.DecodeMedia(payload);
        await Frames.WriteAsync(tls, payload, Frames.MediaLimit + Frames.MediaHeaderBytes, timeout.Token);
        app.Logger.LogInformation("Phase 0 {Channel} authenticated echo completed using {Tls}", channel, tls.SslProtocol);
    }
    catch (AuthenticationException e) { app.Logger.LogWarning("TLS identity rejected: {ErrorType}", e.GetType().Name); }
    catch (IOException e) { app.Logger.LogWarning("Carrier ended: {ErrorType}", e.GetType().Name); }
    catch (WebSocketException e) { app.Logger.LogWarning("Probe carrier disconnected: {ErrorType}", e.GetType().Name); }
    catch (JsonException e) { app.Logger.LogWarning("Invalid probe message: {ErrorType}", e.GetType().Name); }
    catch (OperationCanceledException) { app.Logger.LogWarning("Probe connection timeout/disconnect"); }
});
Console.WriteLine("Phase 0 local channel probe ready on loopback port 7443. No remote control/capture endpoints.");
await app.RunAsync();
}
finally
{
    // Remove only files we created, including startup failure. Never recursively delete the caller's folder.
    foreach (var path in ownedFiles)
    {
        try { File.Delete(path); }
        catch (IOException) { Console.Error.WriteLine("Temporary fixture cleanup failed; remove the generated files manually."); }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("Temporary fixture cleanup denied; remove the generated files manually."); }
    }
}
void WritePrivateFixture(string name, byte[] bytes)
{
    var path = Path.Combine(fixtureFolder, name);
    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    ownedFiles.Add(path);
    output.Write(bytes);
}
