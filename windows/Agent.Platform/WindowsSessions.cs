// LapCont — Platform — Real Windows session identity and lock initiation
// License: MIT
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LapCont.Platform;

/// <summary>Stateless WTS/desktop operations. Lock must be called by the authorized interactive companion.</summary>
public static class WindowsSessions
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool LockWorkStation();
    [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int info, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)] private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WtsSession { public int Id; public IntPtr Name; public int State; }
    public static int[] EnumerateSessions()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var memory, out var count)) return Array.Empty<int>();
        try { return Enumerable.Range(0, Math.Min(count, 128)).Select(i => Marshal.PtrToStructure<WtsSession>(memory + i * Marshal.SizeOf<WtsSession>())).Where(s => s.Id > 0 && User(s.Id) is not null).Select(s => s.Id).ToArray(); }
        finally { WTSFreeMemory(memory); }
    }

    public static int CurrentSessionId => Process.GetCurrentProcess().SessionId;
    public static string CurrentSid => WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
    /// <summary>Real WTSINFOEX snapshot after restart. Unsupported/query failure remains unknown.</summary>
    public static string LockState(int session)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, 25, out var data, out var count)) return "unknown";
        try
        {
            // WTSINFOEX union follows DWORD Level with 8-byte alignment; Level1's third DWORD is SessionFlags.
            if (count < 20 || Marshal.ReadInt32(data) != 1 || Marshal.ReadInt32(data, 8) != session) return "unknown";
            return Marshal.ReadInt32(data, 16) switch { 0 => "locked", 1 => "unlocked", _ => "unknown" };
        }
        finally { WTSFreeMemory(data); }
    }
    public static string? User(int session)
    {
        var user = Query(session, 5); var domain = Query(session, 7);
        return string.IsNullOrEmpty(user) ? null : string.IsNullOrEmpty(domain) ? user : domain + "\\" + user;
    }
    private static string? Query(int session, int info)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, info, out var data, out _)) return null;
        try { return Marshal.PtrToStringUni(data); } finally { WTSFreeMemory(data); }
    }
    /// <summary>Returns initiation only. A WTS/SCM session event is required to confirm completion.</summary>
    public static void InitiateLocalLock()
    {
        if (CurrentSessionId == 0 || !Environment.UserInteractive) throw new InvalidOperationException("NO_INTERACTIVE_SESSION");
        if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error(), "LockWorkStation rejected initiation");
    }
}
