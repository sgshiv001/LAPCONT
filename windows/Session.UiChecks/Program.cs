// LapCont — QA — Render our own component without showing a window, connecting IPC or desktop input
// License: MIT
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LapCont.Session;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Expected an output directory");
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app = new Application(); var window = new CompanionWindow(false, preview: true);
        var type = typeof(CompanionWindow); const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var pageType = type.GetNestedType("Page", BindingFlags.NonPublic)!;
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(window)!;
        void Call(string name, params object[] values) { var result = type.GetMethod(name, flags)!.Invoke(window, values); if (result is Task t) t.GetAwaiter().GetResult(); }
        void Go(string page) => Call("Go", Enum.Parse(pageType, page));
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        string Current() => type.GetField("page", flags)!.GetValue(window)!.ToString()!;
        void Render(string filename, int width = 820, int height = 720)
        {
            var body = Field<StackPanel>("body"); body.BeginAnimation(UIElement.OpacityProperty, null); body.Opacity = 1; body.RenderTransform = Transform.Identity;
            var root = (Grid)window.Content; root.Background = window.Background; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, filename)); encoder.Save(stream);
            Check(root.ActualWidth == width && root.ActualHeight == height, $"{filename}: arranged within requested size");
        }
        Check(Field<Button>("back").Visibility == Visibility.Collapsed, "Overview has no redundant Back button"); Render("windows-overview.png");
        Go("Phones"); Render("windows-phones.png"); type.GetField("selected", flags)!.SetValue(window, "preview"); Go("Phone"); Render("windows-phone.png");
        Call("BackAsync"); Check(Current() == "Phones", "Phone Back returns to paired phones"); Call("BackAsync"); Check(Current() == "Home", "Phones Back returns to Overview");
        Go("Settings"); Render("windows-settings.png"); Render("windows-settings-small.png", 760, 570);
        Check(Field<Button>("back").Visibility == Visibility.Visible, "Settings exposes Back"); Call("BackAsync"); Check(Current() == "Home", "Settings Back returns to Overview");
        Call("OpenPairAsync"); Render("windows-pairing.png"); Call("BackAsync"); Check(Current() == "Home" && Field<StackPanel>("pairing").Children.Count == 0, "Pairing Back cancels and clears its content");
        Call("OpenPairAsync"); Call("OpenPairAsync"); Call("RefreshAsync"); Check(Current() == "Pairing", "Regenerating and refreshing pairing does not reparent or lose the page");
        Check(type.GetField("runtime", flags)!.GetValue(window)!.GetType().GetField("worker", flags)!.GetValue(type.GetField("runtime", flags)!.GetValue(window)) is null, "Preview never starts a service connection");
        File.WriteAllText(Path.Combine(output, "windows-ui-checks.json"), JsonSerializer.Serialize(new { status = "passed", passed = checks.Count, checks, desktop_input_used = false, service_connection_started = false, screenshot_source = "off-screen WPF component rendering", generated_utc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Passed {checks.Count} WPF navigation/layout checks; six component previews rendered."); window.Close(); app.Shutdown(); return 0;
    }
}
