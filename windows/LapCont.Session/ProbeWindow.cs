// LapCont — Session — Local consent, visible activity and actual WTS window notifications
// License: MIT
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LapCont.Platform;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace LapCont.Session;

/// <summary>UI-thread-owned local probe window. Worker probes own capture resources; only actual WTS events confirm lock.</summary>
public sealed class ProbeWindow : Window
{
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16) };
    private readonly TextBox events = new() { IsReadOnly = true, Height = 190, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly System.Windows.Forms.NotifyIcon tray;
    private CancellationTokenSource? probe;
    private IntPtr handle;
    private string? evidenceFile;
    public ProbeWindow(string? evidencePath = null, bool runCaptureChecks = false, bool runStopChecks = false, string? speakerDevice = null)
    {
        if (evidencePath is not null)
        {
            evidenceFile = System.IO.Path.GetFullPath(evidencePath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(evidenceFile)!);
            using var created = new System.IO.FileStream(evidenceFile, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read);
        }
        Title = "LapCont — Phase 0 feasibility probes"; Width = 680; Height = 760;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "LapCont / Phase 0", FontSize = 26 });
        panel.Children.Add(new TextBlock { Text = "Local probes only. No phone pairing or remote unlock. No media is saved.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Interactive session: {WindowsSessions.CurrentSessionId}. Current lock state: unknown until a WTS event.", Margin = new Thickness(0, 12, 0, 8) });
        Add(panel, "Read camera / encoder capabilities", () => RunAsync(() => Task.Run(MediaProbe.Inventory), false, "inventory"));
        Add(panel, "Observe BLE test beacon (8 seconds)", () => RunAsync(() => BleProbe.RunAsync(TimeSpan.FromSeconds(8), probe!.Token), false, "ble"));
        Add(panel, "Start camera capture / H.264 probe (3 seconds)", () => RunAsync(() => MediaProbe.CaptureAsync(probe!.Token), true, "camera"));
        Add(panel, "Start microphone / Opus probe (1.5 seconds)", () => RunAsync(() => AudioProbe.MicrophoneAsync(probe!.Token), true, "microphone"));
        Add(panel, "Test speaker with quiet generated tone (1 second)", () => RunAsync(() => AudioProbe.PlaybackAsync(probe!.Token, speakerDevice), false, "speaker"));
        Add(panel, "Stop active probe", () => { StopActiveProbe(); return Task.CompletedTask; });
        Add(panel, "Lock this Windows session", () =>
        {
            try { WindowsSessions.InitiateLocalLock(); status.Text = "Lock initiation accepted. Completion requires a Windows session-lock event."; WriteEvidence("lock_initiation", new { accepted = true, completion = "pending_windows_event" }); }
            catch (Win32Exception e) { status.Text = $"Lock initiation failed: Windows error {e.NativeErrorCode}"; WriteEvidence("lock_initiation_failed", new { windows_error = e.NativeErrorCode }); }
            catch (InvalidOperationException e) { status.Text = e.Message; WriteEvidence("lock_initiation_failed", new { reason = e.Message }); }
            return Task.CompletedTask;
        });
        panel.Children.Add(status); panel.Children.Add(events); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "LapCont Phase 0 — idle", Visible = true };
        tray.DoubleClick += (_, _) => { Show(); Activate(); };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show probes", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("Stop", null, (_, _) => StopActiveProbe());
        menu.Items.Add("Exit", null, (_, _) => Close()); tray.ContextMenuStrip = menu;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
            if (!WTSRegisterSessionNotification(handle, 0)) { var error = Marshal.GetLastWin32Error(); status.Text = $"WTS registration failed: {error}"; WriteEvidence("wts_registration", new { registered = false, windows_error = error }); }
            else { status.Text = "WTS registered with this real HWND. Lock/unlock events will appear below."; WriteEvidence("wts_registration", new { registered = true, scope = "this_session" }); }
        };
        ContentRendered += async (_, _) =>
        {
            if (runStopChecks)
            {
                await RunStopCheckAsync(() => MediaProbe.CaptureAsync(probe!.Token), "camera_stop_check");
                if (IsVisible) await RunAsync(() => MediaProbe.CaptureAsync(probe!.Token), true, "camera_after_stop");
                if (IsVisible) await RunStopCheckAsync(() => AudioProbe.MicrophoneAsync(probe!.Token), "microphone_stop_check");
                if (IsVisible) await RunAsync(() => AudioProbe.MicrophoneAsync(probe!.Token), true, "microphone_after_stop");
                if (IsVisible) await RunAsync(() => AudioProbe.PlaybackAsync(probe!.Token, speakerDevice), false, "speaker");
            }
            else if (runCaptureChecks)
            {
                await RunAsync(() => MediaProbe.CaptureAsync(probe!.Token), true, "camera");
                if (IsVisible) await RunAsync(() => AudioProbe.MicrophoneAsync(probe!.Token), true, "microphone");
            }
            else if (speakerDevice is not null) await RunAsync(() => AudioProbe.PlaybackAsync(probe!.Token, speakerDevice), false, "speaker");
        };
        Closed += (_, _) => { probe?.Cancel(); if (handle != IntPtr.Zero) WTSUnRegisterSessionNotification(handle); tray.Dispose(); WriteEvidence("window_closed", new { capture_stop_requested = true }); };
    }
    private void Add(StackPanel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(8), HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) => await action(); panel.Children.Add(button);
    }
    private async Task RunAsync(Func<Task<object>> action, bool capture, string name)
    {
        if (probe is not null) { status.Text = "Stop the active probe before starting another."; return; }
        using var active = new CancellationTokenSource(TimeSpan.FromSeconds(15)); probe = active;
        status.Text = capture ? "● LIVE CAPTURE PROBE — Stop is available locally. No recording." : "Probe running…";
        tray.Text = capture ? "LapCont Phase 0 — LIVE CAPTURE" : "LapCont Phase 0 — probing";
        WriteEvidence("probe_started", new { probe = name, capture });
        try { var result = await action(); status.Text = System.Text.Json.JsonSerializer.Serialize(result); WriteEvidence("probe_completed", new { probe = name, result }); }
        catch (OperationCanceledException) { status.Text = "Probe stopped or timed out; capture resources released."; WriteEvidence("probe_cancelled", new { probe = name }); }
        catch (Exception e) // UI boundary: expose failure, release resources in the probe, never claim success.
        { status.Text = $"Probe failed: {e.GetType().Name}, HRESULT 0x{e.HResult:X8}, stage: {e.Data["probe_stage"]}"; events.AppendText($"{DateTimeOffset.UtcNow:O} probe failure {e.GetType().Name}\n"); WriteEvidence("probe_failed", new { probe = name, error_type = e.GetType().Name, hresult = $"0x{e.HResult:X8}", probe_stage = e.Data["probe_stage"] }); }
        finally { probe = null; tray.Text = "LapCont Phase 0 — idle"; }
    }
    private void StopActiveProbe()
    {
        var active = probe is not null;
        WriteEvidence("stop_requested", new { active_probe = active });
        probe?.Cancel(); status.Text = active ? "Stop requested. Waiting for capture resource release." : "No active probe.";
    }
    private async Task RunStopCheckAsync(Func<Task<object>> action, string name)
    {
        // Exercise the same local Stop action against the real driver/codecs after a bounded interval.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) => { timer.Stop(); StopActiveProbe(); };
        timer.Start();
        try { await RunAsync(action, true, name); }
        finally { timer.Stop(); }
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x02B1 || lParam.ToInt32() != WindowsSessions.CurrentSessionId) return IntPtr.Zero;
        var reason = wParam.ToInt32() switch { 7 => "locked", 8 => "unlocked", 6 => "logged_off", _ => "session_event_" + wParam.ToInt32() };
        if (reason is "locked" or "logged_off") probe?.Cancel();
        events.AppendText($"{DateTimeOffset.UtcNow:O} verified WTS event: session {lParam.ToInt32()} {reason}; actual user lookup available={WindowsSessions.User(lParam.ToInt32()) is not null}\n");
        WriteEvidence("wts_event", new { reason, verified_user_available = WindowsSessions.User(lParam.ToInt32()) is not null });
        status.Text = $"Windows reported {reason} for session {lParam.ToInt32()}.";
        return IntPtr.Zero;
    }
    // UI-thread-only metadata journal for finite QA runs. No names, media, credentials or synthetic WTS events.
    private void WriteEvidence(string kind, object detail)
    {
        if (evidenceFile is null) return;
        try
        {
            var record = System.Text.Json.JsonSerializer.Serialize(new { observed_at_utc = DateTimeOffset.UtcNow,
                windows_session_id = WindowsSessions.CurrentSessionId, kind, detail });
            System.IO.File.AppendAllText(evidenceFile, record + Environment.NewLine);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        { evidenceFile = null; events.AppendText($"QA metadata log unavailable: {e.GetType().Name}\n"); }
    }
}
