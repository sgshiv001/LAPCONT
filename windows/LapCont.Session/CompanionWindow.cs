// LapCont — Session — Accessible owner controls with animated, contextual navigation
// License: MIT
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LapCont.Core;
using QRCoder;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Image = System.Windows.Controls.Image;
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;

namespace LapCont.Session;

/// <summary>Owner-only UI. The coordinator still authorizes every action. Preview never starts IPC.</summary>
public sealed class CompanionWindow : Window
{
    private enum Page { Home, Phones, Phone, Pairing, Settings }
    private readonly SessionRuntime runtime;
    private readonly bool preview;
    private readonly TextBlock activity = Label("Connecting to the PC service…", 13);
    private readonly TextBlock title = Label("Overview", 26, true);
    private readonly TextBlock subtitle = Label("Your phone. Your PC. Your choice.", 13);
    private readonly StackPanel body = new();
    private readonly StackPanel pairing = new();
    private readonly ScrollViewer scroll;
    private readonly Button back;
    private readonly Dictionary<Page, Button> navigation = new();
    private readonly System.Windows.Forms.NotifyIcon? tray;
    private readonly DispatcherTimer pairTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock countdown = Label("", 13, true);
    private DateTimeOffset pairExpires;
    private Page page;
    private string? selected;
    private JsonElement[] phones = [];
    private Settings? settings;
    private bool quitting, refreshing, pendingApproval;
    private static readonly Brush Ink = ColorBrush("#20263A"), Muted = ColorBrush("#59637A"), Accent = ColorBrush("#5B4EE8");

    public CompanionWindow(bool development, bool preview = false)
    {
        this.preview = preview;
        Title = preview ? "LapCont · UI preview" : "LapCont · PC companion";
        Width = 1040; Height = 790; MinWidth = 760; MinHeight = 570;
        Background = ColorBrush("#F5F6FB"); Foreground = Ink; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources[typeof(Button)] = ButtonStyle();
        runtime = new(development);
        var shell = new Grid(); shell.ColumnDefinitions.Add(new() { Width = new GridLength(196) }); shell.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Content = shell;
        var sidebar = new DockPanel { Margin = new Thickness(20, 28, 18, 20) }; shell.Children.Add(sidebar);
        var sideFooter = Label("Windows sign-in is always required to unlock.\n\nClosing keeps LapCont in the tray.", 12); sideFooter.Margin = new Thickness(8, 20, 4, 8); DockPanel.SetDock(sideFooter, Dock.Bottom); sidebar.Children.Add(sideFooter);
        var nav = new StackPanel(); sidebar.Children.Add(nav);
        nav.Children.Add(Label("LapCont", 27, true)); nav.Children.Add(Label("PC companion", 12));
        nav.Children.Add(new Border { Height = 30 });
        Nav(nav, Page.Home, "Overview"); Nav(nav, Page.Phones, "Paired phones"); Nav(nav, Page.Settings, "Settings");
        var main = new Grid { Margin = new Thickness(8, 24, 28, 18) }; Grid.SetColumn(main, 1); shell.Children.Add(main);
        main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        back = MakeButton("‹  Back", async () => await BackAsync(), false); back.Width = 88; back.Margin = new Thickness(0, 0, 16, 0); back.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetAutomationId(back, "NavigationBack"); header.Children.Add(back);
        var headings = new StackPanel(); headings.Children.Add(title); headings.Children.Add(subtitle); header.Children.Add(headings); main.Children.Add(header);
        scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 8) }; Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 12, 0) }; Grid.SetRow(footer, 2); main.Children.Add(footer);
        activity.TextWrapping = TextWrapping.Wrap; AutomationProperties.SetLiveSetting(activity, AutomationLiveSetting.Polite); footer.Children.Add(activity);
        var stop = MakeButton("Stop all media", async () => { if (preview) { activity.Text = "Preview · no media is active"; return; } await runtime.StopAsync(); }, false);
        stop.Background = ColorBrush("#FCE8EB"); stop.Foreground = ColorBrush("#9C223F"); AutomationProperties.SetName(stop, "STOP all camera, microphone and talk-back"); footer.Children.Add(stop);
        if (!preview)
        {
            tray = new() { Icon = System.Drawing.SystemIcons.Shield, Text = "LapCont · idle", Visible = true };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); });
            var menu = new System.Windows.Forms.ContextMenuStrip(); menu.Items.Add("Open LapCont", null, (_, _) => Dispatcher.Invoke(() => { Show(); Activate(); }));
            menu.Items.Add("STOP media", null, (_, _) => Dispatcher.InvokeAsync(async () => { try { await runtime.StopAsync(); } catch (Exception e) { ShowFailure(e); } }));
            menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(() => { quitting = true; Close(); })); tray.ContextMenuStrip = menu;
        }
        runtime.Activity += text => Dispatcher.BeginInvoke(async () =>
        {
            activity.Text = text; if (tray is not null) tray.Text = text.Length > 63 ? text[..63] : text;
            if (text.StartsWith("Connected to PC service", StringComparison.Ordinal)) try { await RefreshAsync(); } catch (Exception e) { ShowFailure(e); }
        });
        runtime.Notification += value => Dispatcher.BeginInvoke(() => Notification(value));
        pairTimer.Tick += (_, _) =>
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((pairExpires - DateTimeOffset.UtcNow).TotalSeconds)); countdown.Text = $"Code expires in {seconds}s";
            if (seconds == 0) { pairTimer.Stop(); pairing.Children.Clear(); pendingApproval = false; pairing.Children.Add(Label("This code expired. Generate a fresh code to try again.", 16, true)); Action(pairing, "Generate a new code", OpenPairAsync); if (page == Page.Pairing) Render(); }
        };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Escape && page != Page.Home) { e.Handled = true; await BackAsync(); } };
        Closing += (_, e) => { if (!preview && !quitting) { e.Cancel = true; Hide(); } };
        Closed += async (_, _) => { pairTimer.Stop(); tray?.Dispose(); await runtime.DisposeAsync(); System.Windows.Application.Current.Shutdown(); };
        Loaded += (_, _) => { if (!preview) runtime.Start(); };
        if (preview)
        {
            settings = new();
            phones = [JsonSerializer.SerializeToElement(new { phone_id = "preview", name = "My phone", grants = 0, proximity_enabled = false })];
            activity.Text = "UI preview · example phone · changes are not saved";
        }
        Render();
    }

    private static Brush ColorBrush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    private static TextBlock Label(string text, double size = 14, bool bold = false) => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = bold ? Ink : Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private static Style ButtonStyle() => (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Setter Property="Background" Value="#EAE8FF"/><Setter Property="Foreground" Value="#4034B6"/>
          <Setter Property="Padding" Value="16,12"/><Setter Property="MinHeight" Value="46"/>
          <Setter Property="HorizontalContentAlignment" Value="Left"/><Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" CornerRadius="12" BorderThickness="2" BorderBrush="Transparent" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.88"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="surface" Property="Opacity" Value="0.72"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#7865F4"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    private Button MakeButton(string text, Func<Task> action, bool primary = true)
    {
        var b = new Button { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 5, 0, 5) };
        if (primary) { b.Background = Accent; b.Foreground = Brushes.White; }
        b.Click += async (_, _) => { b.IsEnabled = false; try { await action(); } catch (Exception e) { ShowFailure(e); } finally { b.IsEnabled = true; } }; return b;
    }
    private void Action(Panel parent, string text, Func<Task> action, bool primary = true) => parent.Children.Add(MakeButton(text, action, primary));
    private void ShowFailure(Exception e) => activity.Text = e is ArgumentException ? e.Message : e is CommandFailure c ? $"Action unavailable: {c.Code}. Refresh and try again." : "The PC service is unavailable. Check that LapCont Service is running, then refresh.";
    private void Nav(Panel parent, Page destination, string text)
    {
        var b = MakeButton(text, async () => { await LeavePairAsync(); page = destination; selected = null; Render(); }, false); navigation[destination] = b; parent.Children.Add(b);
    }
    private StackPanel Card(string heading, string? hint = null)
    {
        var panel = new StackPanel(); panel.Children.Add(Label(heading, 19, true)); if (hint is not null) panel.Children.Add(Label(hint));
        body.Children.Add(new Border { Background = Brushes.White, BorderBrush = ColorBrush("#E1E5F0"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16), Child = panel }); return panel;
    }
    private void Render(bool backwards = false)
    {
        if (pairing.Parent is Panel previousParent) previousParent.Children.Remove(pairing);
        body.Children.Clear(); scroll.ScrollToTop(); back.Visibility = page == Page.Home ? Visibility.Collapsed : Visibility.Visible;
        title.Text = page switch { Page.Phones => "Paired phones", Page.Phone => "Phone permissions", Page.Pairing => "Pair a phone", Page.Settings => "Settings", _ => "Overview" };
        subtitle.Text = page switch { Page.Pairing => "Scan. Confirm. Connect.", Page.Phone => "Choose what this phone can use.", Page.Settings => "Privacy and nearby protection.", _ => "Your phone. Your PC. Your choice." };
        foreach (var (p, button) in navigation) { var active = page == p || (p == Page.Phones && page == Page.Phone); button.Background = active ? Accent : Brushes.Transparent; button.Foreground = active ? Brushes.White : Muted; }
        if (preview) body.Children.Add(Label("PREVIEW · Example data. Controls show the layout without changing your PC.", 12, true));
        switch (page)
        {
            case Page.Home:
                var hero = new StackPanel { Margin = new Thickness(26) }; var h = Label("Your PC, connected.", 30, true); h.Foreground = Brushes.White; hero.Children.Add(h);
                var description = Label("Bring your phone closer to your PC. Keep every permission in your hands.", 16); description.Foreground = ColorBrush("#E3DEFF"); hero.Children.Add(description);
                var status = Label(settings is null ? "Waiting for the PC service" : $"{phones.Length} paired phone{(phones.Length == 1 ? "" : "s")} · nearby auto-lock {(settings.ProximityPaused ? "paused" : "enabled")}", 13); status.Foreground = Brushes.White; hero.Children.Add(status);
                body.Children.Add(new Border { CornerRadius = new CornerRadius(24), Background = new LinearGradientBrush(Color.FromRgb(40, 46, 90), Color.FromRgb(89, 72, 204), 25), Child = hero, Margin = new Thickness(0, 0, 0, 20) });
                var connect = Card("Start with your phone", "Pair once, then connect from LapCont on the same Wi-Fi."); Action(connect, "Pair a phone", OpenPairAsync);
                var manage = Card("You're in control", "Allow camera, microphone and talk-back separately for each phone."); Action(manage, "Manage paired phones", () => Go(Page.Phones), false); Action(manage, "Privacy and nearby settings", () => Go(Page.Settings), false);
                body.Children.Add(Label("Media is live only. Returning nearby can send a reminder; Windows never unlocks automatically.", 12)); break;
            case Page.Phones:
                var list = Card("Your trusted phones", "Open a phone to review its permissions.");
                if (phones.Length == 0) list.Children.Add(Label(settings is null ? "Connect to the service and refresh to see your phones." : "No phones paired yet. Add your first phone below."));
                foreach (var phone in phones) { var id = phone.GetProperty("phone_id").GetString()!; Action(list, phone.GetProperty("name").GetString()! + "   ›", () => { selected = id; return Go(Page.Phone); }, false); }
                Action(list, "Pair a phone", OpenPairAsync); Action(list, "Refresh paired phones and permissions", RefreshAsync, false); break;
            case Page.Phone: PhoneDetails(); break;
            case Page.Pairing: var pair = Card(pendingApproval ? "Confirm this phone" : "Scan with LapCont on your phone", "Keep the code private. You approve every permission here."); pair.Children.Add(pairing); break;
            case Page.Settings: OwnerSettings(); break;
        }
        if (SystemParameters.ClientAreaAnimation)
        {
            var transform = new TranslateTransform(); body.RenderTransform = transform;
            body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(backwards ? -14 : 14, 0, TimeSpan.FromMilliseconds(180)));
        }
    }
    private Task Go(Page next) { page = next; Render(); return Task.CompletedTask; }
    private async Task BackAsync() { await LeavePairAsync(); page = page == Page.Phone ? Page.Phones : Page.Home; selected = null; Render(true); }
    private async Task LeavePairAsync()
    {
        if (page != Page.Pairing) return; pairTimer.Stop(); pairing.Children.Clear(); pendingApproval = false;
        if (!preview) try { await runtime.LocalAsync("cancel_pair", new { }); } catch (Exception e) { ShowFailure(e); }
    }
    private async Task RefreshAsync()
    {
        if (refreshing) return; refreshing = true;
        try
        {
            if (!preview) { var result = await runtime.LocalAsync("devices", new { }); phones = result.GetProperty("devices").EnumerateArray().Select(x => x.Clone()).ToArray(); settings = result.GetProperty("settings").Deserialize<Settings>(Wire.Options)!; }
            if (page != Page.Pairing) Render();
        }
        finally { refreshing = false; }
    }
    private async Task OpenPairAsync()
    {
        if (preview) { pairing.Children.Clear(); pairing.Children.Add(Label("Your private QR code appears here when connected to the PC service.", 16)); page = Page.Pairing; Render(); return; }
        var result = await runtime.LocalAsync("open_pair", new { }); pairing.Children.Clear(); pendingApproval = false;
        var json = result.GetProperty("qr").GetRawText();
        using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(json, QRCodeGenerator.ECCLevel.M); using var code = new PngByteQRCode(data);
        using var stream = new MemoryStream(code.GetGraphic(5)); var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream; image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        pairing.Children.Add(new Image { Source = image, Width = 260, Height = 260, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 16) });
        pairing.Children.Add(countdown); pairing.Children.Add(Label("1. Open LapCont on your phone and tap Add a PC.\n2. Scan this code.\n3. Confirm the phone's identity here."));
        var field = Field(json); field.Name = "PairingQrText"; field.IsReadOnly = true; field.TextWrapping = TextWrapping.Wrap; field.MaxHeight = 90; field.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        pairing.Children.Add(new Expander { Header = "Use the code as text instead", Content = field, Margin = new Thickness(0, 10, 0, 10) });
        Action(pairing, "Cancel pairing", BackAsync, false);
        pairExpires = DateTimeOffset.UtcNow.AddSeconds(result.GetProperty("qr").GetProperty("expires_in_seconds").GetInt32()); countdown.Text = "Code expires in 60s"; pairTimer.Start(); page = Page.Pairing; Render();
    }
    private void Notification(JsonElement value)
    {
        if (value.GetProperty("kind").GetString() == "unlock_request") { tray?.ShowBalloonTip(8000, "LapCont", "Your phone requested unlock. Sign in normally at this PC.", System.Windows.Forms.ToolTipIcon.Info); return; }
        if (value.GetProperty("kind").GetString() != "pairing_pending") return;
        // The same enrollment deadline applies after scanning; keep the expiry timer running.
        pendingApproval = true; pairing.Children.Clear(); var pin = value.GetProperty("pin").GetString()!;
        pairing.Children.Add(Label($"{value.GetProperty("phone_name").GetString()} wants to pair", 21, true)); pairing.Children.Add(Label($"Compare this phone identity with your phone:\n{pin}")); pairing.Children.Add(countdown);
        var boxes = PermissionBoxes(pairing, 0);
        Action(pairing, "Approve this phone with selected permissions", async () => { await runtime.LocalAsync("confirm_pair", new { pin, grants = Bits(boxes) }); pairTimer.Stop(); pairing.Children.Clear(); pendingApproval = false; activity.Text = "Phone approved. Pairing completes on the phone."; page = Page.Phones; await RefreshAsync(); });
        Action(pairing, "Reject", BackAsync, false); page = Page.Pairing; Render(); Show(); Activate();
    }
    private static CheckBox Toggle(Panel parent, string label, bool enabled)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = enabled, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 4, 0, 4), MinHeight = 40, VerticalContentAlignment = VerticalAlignment.Center }; parent.Children.Add(box); return box;
    }
    private static CheckBox[] PermissionBoxes(Panel parent, int flags)
    {
        var labels = new[] { "Lock / Request unlock", "Bluetooth proximity", "Camera", "Microphone", "Phone talk-back" };
        return labels.Select((label, i) => Toggle(parent, label, (flags & (1 << i)) != 0)).ToArray();
    }
    private static int Bits(CheckBox[] boxes) => boxes.Select((b, i) => b.IsChecked == true ? 1 << i : 0).Sum();
    private static TextBox Field(string text = "") => new() { Text = text, MinHeight = 42, Padding = new Thickness(10), Margin = new Thickness(0, 2, 0, 12), BorderBrush = ColorBrush("#D8DCEC"), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
    private void PhoneDetails()
    {
        var phone = phones.FirstOrDefault(p => p.GetProperty("phone_id").GetString() == selected);
        if (phone.ValueKind == JsonValueKind.Undefined) { Card("This phone is no longer paired", "Return to your phones and refresh."); return; }
        var id = selected!; var panel = Card(phone.GetProperty("name").GetString()!, "Changes end this phone's active connections. It can reconnect with its updated permissions.");
        var boxes = PermissionBoxes(panel, phone.GetProperty("grants").GetInt32()); var proximity = Toggle(panel, "Participates in automatic proximity locking", phone.GetProperty("proximity_enabled").GetBoolean());
        Action(panel, "Save permissions", async () => { if (preview) { activity.Text = "Preview · permissions were not changed"; return; } await runtime.LocalAsync("permissions", new { phone_id = id, grants = Bits(boxes), proximity_enabled = proximity.IsChecked == true }); activity.Text = "Phone permissions saved"; await RefreshAsync(); });
        var relayPanel = new StackPanel(); relayPanel.Children.Add(Label("Your operator supplies the relay address and a separate agent credential for this phone.")); relayPanel.Children.Add(Label("Relay address (wss://)", 13, true)); var url = Field(); relayPanel.Children.Add(url);
        relayPanel.Children.Add(Label("Agent credential", 13, true)); var credential = new PasswordBox { MinHeight = 42, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) }; relayPanel.Children.Add(credential);
        Action(relayPanel, "Save relay route", async () => { if (preview) { activity.Text = "Preview · relay route was not changed"; return; } try { await runtime.LocalAsync("relay_route", new { phone_id = id, url = url.Text.Trim(), agent_credential = credential.Password }); activity.Text = "Relay route saved"; } finally { credential.Clear(); } });
        var connection = Card("Optional remote connection"); connection.Children.Add(new Expander { Header = "Self-hosted relay settings", Content = relayPanel });
        var remove = Card("Remove this phone", "The phone will need a new pairing to connect again.");
        Action(remove, "Revoke this phone on the PC", async () => { if (preview) { activity.Text = "Preview · example phone was not removed"; return; } if (MessageBox.Show(this, "Remove this phone and end its access to this PC?", "Remove paired phone", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return; await runtime.LocalAsync("revoke", new { phone_id = id }); page = Page.Phones; selected = null; await RefreshAsync(); }, false);
    }
    private void OwnerSettings()
    {
        if (settings is null) { var unavailable = Card("Waiting for the PC service", "Settings appear after a successful refresh."); Action(unavailable, "Refresh settings", RefreshAsync); return; }
        var current = settings; var privacy = Card("Privacy", "Every phone still needs its own permission for each feature.");
        var paused = Toggle(privacy, "Pause proximity auto-lock", current.ProximityPaused);
        var locked = Toggle(privacy, "Allow explicitly permitted media while Windows is locked", current.AllowMediaWhileLocked);
        var nearby = Card("Nearby protection", "Calibrate beside your PC from the phone. Bluetooth signal is an estimate, not a distance measurement.");
        TextBox Number(Panel parent, string label, int value) { parent.Children.Add(Label(label, 13, true)); var field = Field(value.ToString()); parent.Children.Add(field); return field; }
        var delay = Number(nearby, "Weak-signal delay · 10–300 seconds", current.LockDelaySeconds);
        var grace = Number(nearby, "Missing-signal grace · 30–300 seconds", current.MissingBeaconGraceSeconds);
        var advanced = new StackPanel(); advanced.Children.Add(Label("Changing the away threshold overrides saved calibration."));
        var away = Number(advanced, "Away threshold · −100 to −30 dBm", current.RssiAwayThreshold);
        var hysteresis = Number(advanced, "Return hysteresis · 3–20 dB", current.RssiReturnHysteresisDb); nearby.Children.Add(new Expander { Header = "Advanced signal settings", Content = advanced, Margin = new Thickness(0, 8, 0, 8) });
        var audio = Card("Talk-back speaker", "Leave blank to use your Windows default speaker."); var speaker = Field(current.SpeakerDevice ?? ""); audio.Children.Add(speaker);
        int Read(TextBox field, string name, int min, int max) { if (!int.TryParse(field.Text, out var value) || value < min || value > max) throw new ArgumentException($"{name}: enter a whole number from {min} to {max}."); return value; }
        Action(body, "Save owner privacy and proximity settings", async () =>
        {
            if (preview) { activity.Text = "Preview · settings were not changed"; return; }
            var next = current with { ProximityPaused = paused.IsChecked == true, AllowMediaWhileLocked = locked.IsChecked == true, SpeakerDevice = string.IsNullOrWhiteSpace(speaker.Text) ? null : speaker.Text.Trim(), RssiAwayThreshold = Read(away, "Away threshold", -100, -30), RssiReturnHysteresisDb = Read(hysteresis, "Return hysteresis", 3, 20), LockDelaySeconds = Read(delay, "Weak-signal delay", 10, 300), MissingBeaconGraceSeconds = Read(grace, "Missing-signal grace", 30, 300) };
            next.Validate(); await runtime.LocalAsync("settings", next); activity.Text = "Settings saved"; await RefreshAsync();
        });
        Action(body, "Refresh settings", RefreshAsync, false);
    }
}
