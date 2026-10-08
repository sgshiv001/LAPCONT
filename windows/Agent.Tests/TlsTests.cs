// LapCont — Tests — Real platform mutual TLS and wrong-identity rejection
// License: MIT
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using LapCont.Protocol;
using LapCont.Security;
using Xunit;

namespace LapCont.Tests;

/// <summary>Loopback integration tests using real Schannel. Each test owns its listener and ephemeral identities.</summary>
public sealed class TlsTests
{
    [Fact] public async Task FreshMutualTlsExchangesPayload()
    {
        using var pc = Identity.Create("pc"); using var phone = Identity.Create("phone");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var ct = new CancellationTokenSource(10000);
        var server = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync(ct.Token);
            await using var tls = await TlsTunnel.ServerAsync(tcp.GetStream(), pc, Identity.Fingerprint(phone), ct.Token);
            Assert.True(tls.IsMutuallyAuthenticated);
            var payload = await Frames.ReadAsync(tls, 4096, ct.Token);
            await Frames.WriteAsync(tls, payload, 4096, ct.Token);
        });
        using var client = new TcpClient(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, ct.Token);
        System.Net.Security.SslStream channel;
        try { channel = await TlsTunnel.ClientAsync(client.GetStream(), phone, Identity.Fingerprint(pc), ct.Token); }
        catch (IOException) { await server; throw; }
        await using var ownedChannel = channel;
        Assert.Contains(channel.SslProtocol, new[] { SslProtocols.Tls12, SslProtocols.Tls13 });
        await Frames.WriteAsync(channel, "protected"u8.ToArray(), 4096, ct.Token);
        Assert.Equal("protected"u8.ToArray(), await Frames.ReadAsync(channel, 4096, ct.Token));
        await server;
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task WrongPeerIsRejected(bool wrongPc)
    {
        using var pc = Identity.Create("pc"); using var phone = Identity.Create("phone"); using var wrong = Identity.Create("wrong");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var ct = new CancellationTokenSource(5000);
        var server = Task.Run(async () =>
        {
            using var tcp = await listener.AcceptTcpClientAsync(ct.Token);
            try { await using var tls = await TlsTunnel.ServerAsync(tcp.GetStream(), pc,
                Identity.Fingerprint(wrongPc ? phone : wrong), ct.Token); await Frames.ReadAsync(tls, 4096, ct.Token); }
            catch (AuthenticationException) { } catch (IOException) { } catch (OperationCanceledException) { }
        });
        using var client = new TcpClient(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, ct.Token);
        var rejected = false;
        try
        {
            await using var tls = await TlsTunnel.ClientAsync(client.GetStream(), phone,
                Identity.Fingerprint(wrongPc ? wrong : pc), ct.Token);
            await Frames.WriteAsync(tls, "probe"u8.ToArray(), 4096, ct.Token);
            await Frames.ReadAsync(tls, 4096, ct.Token);
        }
        catch (AuthenticationException) { rejected = true; } catch (IOException) { rejected = true; }
        Assert.True(rejected); await server;
    }
}
