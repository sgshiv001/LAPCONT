// LapCont — Service — TLS LAN listener and nested mutual TLS endpoints
// License: MIT
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LapCont.Core;
using LapCont.Platform;
using LapCont.Protocol;
using LapCont.Security;

namespace LapCont.Service;

/// <summary>Hosts the production coordinator. Lifecycle is owned by SCM or explicit console development mode.</summary>
public sealed class ProductHost(bool development, string? dataDirectory = null) : IAsyncDisposable
{
    private WebApplication? app;
    private readonly CancellationTokenSource life = new();
    private Task? supervisor;
    public Coordinator Coordinator { get; } = new(new ProtectedStore(dataDirectory ?? Path.Combine(Environment.GetFolderPath(development ? Environment.SpecialFolder.LocalApplicationData : Environment.SpecialFolder.CommonApplicationData), "LapCont", development ? "Development" : "Service"), !development));
    private readonly SemaphoreSlim handshakes = new(8);
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole(); builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxConcurrentConnections = 32;
            k.Listen(IPAddress.Any, Coordinator.Settings.Port, l => l.UseHttps(h => { h.ServerCertificate = Coordinator.Identity; h.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13; }));
        });
        app = builder.Build(); app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        Coordinator.RelayEndpoint = (channel, phone, socket, prefix, token) => ProcessSocketAsync(channel, socket, prefix, token, phone);
        app.MapGet("/health", () => Results.Json(new { status = "ok" }));
        app.Map("/{channel}", HandleAsync);
        supervisor = Task.Run(() => Coordinator.RunAsync(life.Token));
        await app.StartAsync(life.Token); Coordinator.Log("coordinator started; lock state reconstructed through WTSINFOEX or explicitly unknown");
    }
    private async Task HandleAsync(HttpContext context)
    {
        var channel = context.Request.RouteValues["channel"]?.ToString();
        if (!context.WebSockets.IsWebSocketRequest || channel is not ("pair" or "control" or "media")) { context.Response.StatusCode = 400; return; }
        if (handshakes.CurrentCount == 0) { context.Response.StatusCode = 429; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await ProcessSocketAsync(channel!, socket, Array.Empty<byte>(), context.RequestAborted);
    }
    public async Task ProcessSocketAsync(string channel, System.Net.WebSockets.WebSocket socket, byte[] prefix, CancellationToken parent, string? routePhone = null)
    {
        if (!await handshakes.WaitAsync(0, parent)) return;
        using var life = CancellationTokenSource.CreateLinkedTokenSource(parent, this.life.Token); life.CancelAfter(TimeSpan.FromMinutes(10));
        PeerConnection? peer = null; var ownsPeer = false; var handshakeReleased = false;
        try
        {
            await using var carrier = new TunnelBudget(new PrefixedStream(new WebSocketStream(socket), prefix));
            string? peerPin = null;
            await using var tls = new SslStream(carrier, false, (_, cert, _, _) =>
            {
                if (cert is null) return false;
                var pin = LapCont.Security.Identity.Fingerprint(cert);
                if (!LapCont.Security.Identity.Matches(cert, pin)) return false;
                var enrolled = Coordinator.FindByPin(pin);
                if (channel != "pair" && (enrolled is null || routePhone is not null && enrolled.PhoneId != routePhone) || channel == "pair" && !Coordinator.Pairing.IsOpen) return false;
                peerPin = pin; return true;
            });
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(10));
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Coordinator.Identity, ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, AllowRenegotiation = false, AllowTlsResume = false, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, handshake.Token);
                var cipher = tls.NegotiatedCipherSuite.ToString();
                if (tls.SslProtocol == SslProtocols.Tls12 && !(cipher.StartsWith("TLS_ECDHE_", StringComparison.Ordinal) && cipher.Contains("_GCM_", StringComparison.Ordinal)))
                    throw new AuthenticationException("TLS 1.2 requires ECDHE/GCM");
            }
            if (peerPin is null) throw new AuthenticationException("Client identity missing"); carrier.Authenticated(); handshakes.Release(); handshakeReleased = true;
            if (channel == "pair")
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(life.Token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
                await Frames.WriteAsync(tls, Wire.Encode(new { protocol = "LPC1", kind = "context", channel, pc_id = Coordinator.PcId }), Frames.ControlLimit, deadline.Token);
                using var doc = Frames.ParseControl(await Frames.ReadAsync(tls, Frames.ControlLimit, deadline.Token));
                object result;
                try { result = await Coordinator.EnrollAsync(peerPin, doc.RootElement, deadline.Token); }
                catch (CommandFailure e) { result = new { protocol = "LPC1", kind = "pair_result", ok = false, code = e.Code, detail = e.Message }; }
                await Frames.WriteAsync(tls, Wire.Encode(result), Frames.ControlLimit, deadline.Token); return;
            }
            var enrollment = Coordinator.FindByPin(peerPin) ?? throw new AuthenticationException("Revoked phone");
            if (channel == "control")
            {
                peer = new(enrollment.PhoneId, peerPin, life.Token); ownsPeer = true; await Coordinator.RegisterAsync(peer, life.Token);
                await Frames.WriteAsync(tls, Wire.Encode(new { protocol = "LPC1", kind = "context", channel, pc_id = Coordinator.PcId, phone_id = peer.PhoneId,
                    connection_session_id = peer.Id, media_attachment = peer.Attachment, pc_time_utc = DateTimeOffset.UtcNow }), Frames.ControlLimit, life.Token);
                await peer.ControlAsync(tls, Coordinator);
            }
            else
            {
                using var attachment = CancellationTokenSource.CreateLinkedTokenSource(life.Token); attachment.CancelAfter(TimeSpan.FromSeconds(10));
                using var hello = Frames.ParseControl(await Frames.ReadAsync(tls, Frames.ControlLimit, attachment.Token)); var h = hello.RootElement;
                Command.Exact(h, "protocol", "kind", "pc_id", "phone_id", "connection_session_id", "media_attachment");
                if (Command.Text(h, "protocol", 4) != "LPC1" || Command.Text(h, "kind", 16) != "media_attach" || Command.Text(h, "pc_id", 64) != Coordinator.PcId || Command.Text(h, "phone_id", 64) != enrollment.PhoneId)
                    throw new AuthenticationException("Wrong attachment identity");
                peer = await Coordinator.AttachAsync(Command.Text(h, "media_attachment", 64), enrollment.PhoneId, Command.Text(h, "connection_session_id", 32), life.Token);
                peer.Media = tls;
                await Frames.WriteAsync(tls, Wire.Encode(new { protocol = "LPC1", kind = "context", channel, pc_id = Coordinator.PcId, phone_id = peer.PhoneId, connection_session_id = peer.Id }), Frames.ControlLimit, life.Token);
                await peer.MediaAsync(tls, Coordinator, life.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Coordinator.Log($"channel={channel} error={e.GetType().Name} hresult={e.HResult:X8} inner={e.InnerException?.GetType().Name}"); }
        finally
        {
            if (!handshakeReleased) handshakes.Release();
            if (peer is not null) { peer.Close(); if (peer.CloseReason is { } reason) Coordinator.Log($"channel={channel} error={reason}"); if (ownsPeer) { await Coordinator.DisconnectedAsync(peer); peer.Dispose(); } }
        }
    }
    public async ValueTask DisposeAsync()
    {
        life.Cancel(); if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
        if (supervisor is not null) try { await supervisor; } catch (OperationCanceledException) { }
        await Coordinator.DisposeAsync(); life.Dispose(); handshakes.Dispose();
    }
}
