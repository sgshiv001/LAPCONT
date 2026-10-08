// LapCont — Platform — Per-session IPC identity feasibility
// License: MIT
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LapCont.Platform;

/// <summary>Stateless ACL/identity helpers. Caller owns streams and serialized control/media queues.</summary>
public static class SessionPipe
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientSessionId(IntPtr pipe, out uint session);
    public static NamedPipeServerStream Create(int session, SecurityIdentifier user, string channel = "Control")
    {
        if (session <= 0 || channel is not ("Control" or "Media")) throw new ArgumentException("Invalid session/channel");
        var acl = new PipeSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create($"LapCont.Session.{session}.{channel}", PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 8192, 8192, acl);
    }
    public static bool VerifyClient(NamedPipeServerStream pipe, int session, SecurityIdentifier expected)
    {
        if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out var actual) || actual != session) return false;
        SecurityIdentifier? user = null;
        pipe.RunAsClient(() => user = WindowsIdentity.GetCurrent().User);
        return user == expected;
    }
}
