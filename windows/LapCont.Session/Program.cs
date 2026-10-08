// LapCont — Session — Explicit interactive probe entry point
// License: MIT
using System.Windows;

namespace LapCont.Session;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 0 || args.SequenceEqual(new[] { "--development" }) || args.SequenceEqual(new[] { "--ui-preview" }))
        {
            var product = new System.Windows.Application(); product.Run(new CompanionWindow(args.Contains("--development"), args.Contains("--ui-preview"))); return;
        }
        string? evidence = null;
        var captureChecks = false;
        var stopChecks = false;
        string? speakerDevice = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--evidence-file" && i + 1 < args.Length) evidence = args[++i];
            else if (args[i] == "--capture-checks") captureChecks = true;
            else if (args[i] == "--stop-checks") stopChecks = true;
            else if (args[i] == "--speaker-device" && i + 1 < args.Length) speakerDevice = args[++i];
            else throw new ArgumentException("Expected --evidence-file <new metadata log>, --capture-checks, --stop-checks or --speaker-device <name>");
        }
        var app = new System.Windows.Application(); app.Run(new ProbeWindow(evidence, captureChecks, stopChecks, speakerDevice));
    }
}
