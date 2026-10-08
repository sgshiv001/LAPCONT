// LapCont — Security — Standard mutually authenticated TLS over a relay byte stream
// License: MIT
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace LapCont.Security;

/// <summary>Platform TLS factory. One reader/one writer per returned stream; exact paired leaf pins replace CA trust.</summary>
public static class TlsTunnel
{
    public static async Task<SslStream> ServerAsync(Stream transport, X509Certificate2 identity,
        string phoneFingerprint, CancellationToken ct)
    {
        var tls = new SslStream(transport, false, (_, cert, _, _) => Identity.Matches(cert, phoneFingerprint));
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = identity, ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                AllowRenegotiation = false, AllowTlsResume = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);
            return tls;
        }
        catch { await tls.DisposeAsync(); throw; }
    }
    public static async Task<SslStream> ClientAsync(Stream transport, X509Certificate2 identity,
        string pcFingerprint, CancellationToken ct)
    {
        var tls = new SslStream(transport, false, (_, cert, _, _) => Identity.Matches(cert, pcFingerprint));
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost", ClientCertificates = new X509CertificateCollection { identity },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                AllowRenegotiation = false, AllowTlsResume = false,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);
            return tls;
        }
        catch { await tls.DisposeAsync(); throw; }
    }
}
