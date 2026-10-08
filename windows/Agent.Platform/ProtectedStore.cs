// LapCont — Platform — DPAPI identity and restrictive local storage
// License: MIT
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;
using LapCont.Core;

namespace LapCont.Platform;

/// <summary>Single coordinator owner. DPAPI plus explicit filesystem ACLs protect machine/user identities and registry.</summary>
public sealed class ProtectedStore
{
    public string Folder { get; }
    private readonly DataProtectionScope scope;
    public ProtectedStore(string folder, bool machine)
    {
        Folder = Path.GetFullPath(folder); scope = machine ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
        Directory.CreateDirectory(Folder);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        var principals = new List<SecurityIdentifier> { new(WellKnownSidType.LocalSystemSid, null), new(WellKnownSidType.BuiltinAdministratorsSid, null) };
        if (!machine) principals.Add(WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("No user SID"));
        foreach (var p in principals) acl.AddAccessRule(new FileSystemAccessRule(p, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(Folder).SetAccessControl(acl);
    }
    public T? Load<T>(string name) => File.Exists(Path.Combine(Folder, name + ".dpapi")) ?
        JsonSerializer.Deserialize<T>(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Folder, name + ".dpapi")), null, scope), Wire.Options) : default;
    public void Save<T>(string name, T value)
    {
        var path = Path.Combine(Folder, name + ".dpapi"); var temporary = path + ".new";
        var plain = Wire.Encode(value ?? throw new ArgumentNullException(nameof(value)));
        try { File.WriteAllBytes(temporary, ProtectedData.Protect(plain, null, scope)); File.Move(temporary, path, true); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public X509Certificate2 Identity()
    {
        var pfx = Load<byte[]>("identity");
        if (pfx is null)
        {
            using var key = RSA.Create(3072);
            var request = new CertificateRequest("CN=LapCont", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") }, false));
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddDnsName(Environment.MachineName);
            foreach (var ip in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)) san.AddIpAddress(ip);
            request.CertificateExtensions.Add(san.Build());
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
            pfx = cert.Export(X509ContentType.Pfx); Save("identity", pfx);
        }
        try { return new X509Certificate2(pfx, (string?)null, scope == DataProtectionScope.LocalMachine ? X509KeyStorageFlags.MachineKeySet : X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
}
