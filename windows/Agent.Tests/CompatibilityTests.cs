// LapCont — Tests — Certificate validity and real TLS 1.2 compatibility negotiation
// License: MIT
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LapCont.Protocol;
using LapCont.Security;
using Xunit;

namespace LapCont.Tests;

/// <summary>Uses actual certificates and Schannel. A Windows 11 TLS 1.2 run does not establish Windows 10 support.</summary>
public sealed class CompatibilityTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExactPinCannotAcceptAnExpiredOrFutureCertificate(bool future)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=validity-probe", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(future ? now.AddDays(1) : now.AddDays(-2), future ? now.AddDays(2) : now.AddDays(-1));
        Assert.False(Identity.Matches(certificate, Identity.Fingerprint(certificate)));
    }

    [Fact]
    public async Task Tls12NegotiatesMutualAuthenticationAndGcmOnThisHost()
    {
        using var pc = Identity.Create("pc"); using var phone = Identity.Create("phone");
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var tls = await TlsTunnel.ServerAsync(peer.GetStream(), pc, Identity.Fingerprint(phone), deadline.Token);
            Assert.True(tls.IsMutuallyAuthenticated); Assert.Equal(SslProtocols.Tls12, tls.SslProtocol);
            var bytes = await Frames.ReadAsync(tls, Frames.ControlLimit, deadline.Token);
            await Frames.WriteAsync(tls, bytes, Frames.ControlLimit, deadline.Token);
        });
        using var client = new TcpClient(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, deadline.Token);
        await using var channel = new SslStream(client.GetStream(), false, (_, cert, _, _) => Identity.Matches(cert, Identity.Fingerprint(pc)));
        await channel.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost", ClientCertificates = new X509CertificateCollection { phone },
            EnabledSslProtocols = SslProtocols.Tls12, AllowRenegotiation = false, AllowTlsResume = false
        }, deadline.Token);
        Assert.Equal(SslProtocols.Tls12, channel.SslProtocol); Assert.True(channel.IsMutuallyAuthenticated);
        Assert.Contains(channel.NegotiatedCipherSuite, new[] { TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256, TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384 });
        await Frames.WriteAsync(channel, "tls12-real-platform-echo"u8.ToArray(), Frames.ControlLimit, deadline.Token);
        Assert.Equal("tls12-real-platform-echo"u8.ToArray(), await Frames.ReadAsync(channel, Frames.ControlLimit, deadline.Token));
        await server;
    }
}
