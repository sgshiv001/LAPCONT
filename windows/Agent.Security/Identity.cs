// LapCont — Security — Runtime prototype identities and exact certificate pins
// License: MIT
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LapCont.Security;

/// <summary>Stateless TLS certificate utilities; thread safe. Probe identities are temporary, not enrollment.</summary>
public static class Identity
{
    public static X509Certificate2 Create(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") }, false));
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); request.CertificateExtensions.Add(san.Build());
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        // .NET 8 Schannel requires an OS key container. Default import cleans up its temporary
        // container on Dispose; PersistKeySet is deliberately absent. Production storage is Phase 1.
        return new X509Certificate2(temporary.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
    }
    public static string Fingerprint(X509Certificate cert) => Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
    public static bool Matches(X509Certificate? cert, string fingerprint)
    {
        if (cert is null || fingerprint.Length != 64) return false;
        using var typed = new X509Certificate2(cert);
        if (DateTime.UtcNow < typed.NotBefore.ToUniversalTime() || DateTime.UtcNow > typed.NotAfter.ToUniversalTime()) return false;
        try { return CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), Convert.FromHexString(fingerprint)); }
        catch (FormatException) { return false; }
    }
}
