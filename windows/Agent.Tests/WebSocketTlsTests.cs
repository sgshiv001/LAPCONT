// LapCont — Tests — Platform TLS rejection alerts through a real WebSocket carrier
// License: MIT
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Authentication;
using LapCont.Protocol;
using LapCont.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LapCont.Tests;

/// <summary>Actual Kestrel/WebSocket/Schannel boundary. No certificate must produce an authentication failure,
/// including Schannel's synchronous alert write during an asynchronous handshake.</summary>
public sealed class WebSocketTlsTests
{
    [Fact]
    public async Task MissingPhoneFailsAuthenticationWithoutBreakingTheCarrier()
    {
        using var pc = Identity.Create("pc"); using var phone = Identity.Create("phone");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var outcome = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.UseWebSockets();
        app.Map("/probe", async context =>
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await using var carrier = new WebSocketStream(socket);
            try
            {
                await using var tls = await TlsTunnel.ServerAsync(carrier, pc, Identity.Fingerprint(phone), deadline.Token);
                outcome.TrySetResult(null);
            }
            catch (Exception error) { outcome.TrySetResult(error); }
        });
        await app.StartAsync(deadline.Token);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var socket = new ClientWebSocket();
            // Plain loopback WS isolates the inner TLS provider/adapter; the actual channel host uses outer WSS.
            await socket.ConnectAsync(new Uri(address.Replace("http://", "ws://") + "/probe"), deadline.Token);
            await using var carrier = new WebSocketStream(socket);
            await using var tls = new SslStream(carrier, false, (_, cert, _, _) => Identity.Matches(cert, Identity.Fingerprint(pc)));
            var rejected = false;
            try
            {
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    AllowRenegotiation = false, AllowTlsResume = false
                    // Deliberately no phone certificate.
                }, deadline.Token);
                await Frames.ReadAsync(tls, Frames.ControlLimit, deadline.Token);
            }
            catch (AuthenticationException) { rejected = true; }
            catch (IOException) { rejected = true; }
            Assert.True(rejected);
            Assert.IsType<AuthenticationException>(await outcome.Task.WaitAsync(deadline.Token));
        }
        finally { await app.StopAsync(CancellationToken.None); }
    }
}
