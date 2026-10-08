// LapCont — Platform — OS-authenticated service/companion pipes
// License: MIT
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LapCont.Platform;

/// <summary>Stateless local IPC. Service is server; OS token/SID/session checks supplement ACL and first-instance ownership.</summary>
public static class CoordinatorPipe
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientSessionId(IntPtr pipe, out uint session);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint session, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, IntPtr buffer, int size, out int required);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryServiceConfig(IntPtr service, IntPtr buffer, int size, out int required);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr service);
    [StructLayout(LayoutKind.Sequential)] private struct ServiceConfig { public uint Type, Start, Error; public IntPtr Binary, Group; public uint Tag; public IntPtr Dependencies, Account, Name; }
    private static bool IsCoordinatorService(uint pid)
    {
        var manager = OpenSCManager(null, null, 1); if (manager == IntPtr.Zero) return false;
        var service = OpenService(manager, "LapCont", 5); if (service == IntPtr.Zero) { CloseServiceHandle(manager); return false; }
        var status = Marshal.AllocHGlobal(36); IntPtr config = IntPtr.Zero;
        try
        {
            if (!QueryServiceStatusEx(service, 0, status, 36, out _) || Marshal.ReadInt32(status, 28) != pid) return false;
            QueryServiceConfig(service, IntPtr.Zero, 0, out var length); if (length is <= 0 or > 65536) return false;
            config = Marshal.AllocHGlobal(length); if (!QueryServiceConfig(service, config, length, out _)) return false;
            return Marshal.PtrToStringUni(Marshal.PtrToStructure<ServiceConfig>(config).Account) == "LocalSystem";
        }
        finally { Marshal.FreeHGlobal(status); if (config != IntPtr.Zero) Marshal.FreeHGlobal(config); CloseServiceHandle(service); CloseServiceHandle(manager); }
    }
    public static string Name(int session, string channel) => $"LapCont.Coordinator.{session}.{channel}";
    public static string? SessionSid(int session)
    {
        if (WindowsSessions.CurrentSessionId == session && Environment.UserInteractive) return WindowsSessions.CurrentSid;
        if (!WTSQueryUserToken((uint)session, out var token)) return null;
        using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle())) return identity.User?.Value;
    }
    public static NamedPipeServerStream Server(int session, string sid, string channel)
    {
        if (session <= 0 || channel is not ("Control" or "Media")) throw new ArgumentException("Invalid pipe context");
        var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(Name(session, channel), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 8192, 8192, acl);
    }
    public static bool VerifyUser(NamedPipeServerStream pipe, int session, string sid)
    {
        if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out var actual) || actual != session) return false;
        string? actualSid = null; pipe.RunAsClient(() => { using var i = WindowsIdentity.GetCurrent(); actualSid = i.User?.Value; });
        return actualSid == sid;
    }
    public static async Task<NamedPipeClientStream> ConnectAsync(int session, string channel, bool development, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", Name(session, channel), PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        try
        {
            await pipe.ConnectAsync(3000, ct);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var pid)) throw new UnauthorizedAccessException("Cannot verify IPC server");
            if (!IsCoordinatorService(pid))
            {
                if (!development) throw new UnauthorizedAccessException("IPC server PID/account does not match the installed coordinator service");
                using var process = Process.GetProcessById((int)pid);
                if (!OpenProcessToken(process.Handle, 8, out var token)) throw new UnauthorizedAccessException("Cannot query development server identity");
                using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                    if (identity.User?.Value != WindowsSessions.CurrentSid) throw new UnauthorizedAccessException("Development coordinator must run as the same user");
            }
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }
}
