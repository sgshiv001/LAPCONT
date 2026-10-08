// LapCont — Core — Rotating authenticated beacons and monotonic multi-phone policy
// License: MIT
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LapCont.Core;

/// <summary>Stateless beacon codec. Secret is independent of transport identity; 30-second rotating ID, no MAC tracking.</summary>
public static class Beacon
{
    public static byte[] Create(byte[] secret, string pc, string phone, long unixSeconds)
    {
        var epoch = checked((uint)(unixSeconds / 30)); var output = new byte[16]; BinaryPrimitives.WriteUInt32BigEndian(output, epoch);
        HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"LPC1.proximity:{pc}:{phone}:{epoch}")).AsSpan(0, 12).CopyTo(output.AsSpan(4)); return output;
    }
    public static bool Matches(byte[] bytes, byte[] secret, string pc, string phone, long seconds)
    {
        if (bytes.Length != 16) return false;
        var epoch = BinaryPrimitives.ReadUInt32BigEndian(bytes); var current = seconds / 30;
        return Math.Abs((long)epoch - current) <= 1 && CryptographicOperations.FixedTimeEquals(bytes, Create(secret, pc, phone, epoch * 30L));
    }
}

/// <summary>Single-owner filter. Only finalized valid one-second buckets enter the median/calibration.</summary>
public sealed class ProximityPolicy(TimeProvider clock, Settings settings)
{
    private sealed class Phone
    {
        public readonly Queue<(long At, int Rssi)> Samples = new();
        public long? LastSeen, AwaySince; public bool Armed; public string State = "unknown";
        public int Threshold; public int? Median; public readonly List<int> Calibration = new(); public bool Calibrating, Calibrated;
        public long? CalibrationAt, NearSince, LastReminder; public bool ReturnPending, WasAway; public string CalibrationStatus = "not_calibrated";
    }
    private readonly Dictionary<string, Phone> phones = new();
    public bool Healthy { get; private set; }
    public void Configure(IEnumerable<string> eligible)
    {
        var ids = eligible.ToHashSet(); foreach (var id in phones.Keys.Where(id => !ids.Contains(id)).ToArray()) phones.Remove(id);
        foreach (var id in ids) if (!phones.ContainsKey(id)) phones[id] = new() { Threshold = settings.RssiAwayThreshold };
    }
    public void Health(bool healthy)
    {
        if (Healthy == healthy) return; Healthy = healthy;
        foreach (var p in phones.Values) { p.Samples.Clear(); p.LastSeen = p.AwaySince = p.NearSince = null; p.Armed = false; p.Median = null; p.State = "unknown"; p.Calibrating = false; p.Calibration.Clear(); p.ReturnPending = p.WasAway = false; }
    }
    public void RestoreCalibration(string id, int threshold) { if (threshold is < -100 or > -30 || !phones.TryGetValue(id, out var p)) return; p.Threshold = threshold; p.Calibrated = true; p.CalibrationStatus = "calibrated"; }
    public int? Threshold(string id) => phones.TryGetValue(id, out var p) && p.Calibrated ? p.Threshold : null;
    public void Bucket(string id, int rssi, long? observedAt = null)
    {
        if (!Healthy || !phones.TryGetValue(id, out var p) || rssi is < -127 or > -1) return;
        var now = clock.GetTimestamp(); var at = observedAt ?? now;
        if (at > now || clock.GetElapsedTime(at, now) > TimeSpan.FromSeconds(3)) return;
        if (p.LastSeen.HasValue && clock.GetElapsedTime(p.LastSeen.Value, at) < TimeSpan.FromSeconds(1)) return;
        if (p.LastSeen.HasValue && clock.GetElapsedTime(p.LastSeen.Value, at) > TimeSpan.FromSeconds(2)) p.AwaySince = null;
        p.LastSeen = at; p.Samples.Enqueue((at, rssi)); while (p.Samples.Count > 60) p.Samples.Dequeue();
        if (p.Calibrating) { p.Calibration.Add(rssi); if (p.Calibration.Count == 8) { p.Threshold = Math.Clamp(Median(p.Calibration) - 12, -100, -30); p.Calibrating = false; p.Calibrated = true; p.CalibrationStatus = "calibrated"; } }
        var latest = p.Samples.TakeLast(7).ToArray();
        if (latest.Length < 7 || clock.GetElapsedTime(latest[0].At, at) > TimeSpan.FromSeconds(10)) { p.State = "unknown"; return; }
        p.Median = Median(latest.Select(s => s.Rssi));
        if (p.Median < p.Threshold) { p.AwaySince ??= at; p.NearSince = null; if (p.State != "away") p.State = "weak"; }
        else if (p.Median >= p.Threshold + settings.RssiReturnHysteresisDb)
        { if (p.WasAway) { p.ReturnPending = true; p.WasAway = false; } p.AwaySince = null; p.NearSince ??= at; p.State = "near"; if (p.Calibrated) p.Armed = true; }
        else { p.AwaySince = null; p.NearSince = null; }
    }
    public bool Tick()
    {
        foreach (var p in phones.Values)
        {
            if (p.Calibrating && p.CalibrationAt.HasValue && clock.GetElapsedTime(p.CalibrationAt.Value) > TimeSpan.FromSeconds(20)) { p.Calibrating = false; p.CalibrationStatus = "insufficient_data"; }
            if (!Healthy || settings.ProximityPaused || !p.Armed) { p.State = "unknown"; continue; }
            if (p.LastSeen.HasValue && clock.GetElapsedTime(p.LastSeen.Value) >= TimeSpan.FromSeconds(settings.MissingBeaconGraceSeconds)) p.State = "away";
            else if (p.LastSeen.HasValue && clock.GetElapsedTime(p.LastSeen.Value) > TimeSpan.FromSeconds(2)) { p.AwaySince = p.NearSince = null; p.State = "missing"; }
            else if (p.AwaySince.HasValue && clock.GetElapsedTime(p.AwaySince.Value) >= TimeSpan.FromSeconds(settings.LockDelaySeconds)) p.State = "away";
            if(p.State == "away") { p.WasAway = true; p.NearSince = null; }
        }
        return Healthy && !settings.ProximityPaused && phones.Count > 0 && phones.Values.All(p => p.State == "away");
    }
    public object Status(string id) => phones.TryGetValue(id, out var p) ? new { state = p.State, median_rssi = p.Median, threshold = p.Threshold, observer_healthy = Healthy,
        calibration_samples = p.Calibration.Count, calibrating = p.Calibrating, calibration_status = p.CalibrationStatus, armed = p.Armed,
        recent_rssi = p.Samples.TakeLast(8).Select(s => s.Rssi).ToArray(), lock_delay_seconds = settings.LockDelaySeconds, missing_grace_seconds = settings.MissingBeaconGraceSeconds, paused = settings.ProximityPaused } : new { state = "disabled" };
    public string[] Returned()
    {
        var result = new List<string>();
        foreach (var (id, p) in phones) if (Healthy && !settings.ProximityPaused && p.ReturnPending && p.State == "near" && p.LastSeen.HasValue && clock.GetElapsedTime(p.LastSeen.Value) <= TimeSpan.FromSeconds(2) && p.NearSince.HasValue && clock.GetElapsedTime(p.NearSince.Value) >= TimeSpan.FromSeconds(3) &&
            (!p.LastReminder.HasValue || clock.GetElapsedTime(p.LastReminder.Value) >= TimeSpan.FromSeconds(60))) { p.ReturnPending = false; p.LastReminder = clock.GetTimestamp(); result.Add(id); }
        return result.ToArray();
    }
    public void Calibrate(string id, string operation)
    {
        if (!phones.TryGetValue(id, out var p) || !Healthy) throw new CommandFailure("PROXIMITY_UNAVAILABLE", "Enable proximity and a healthy observer first");
        if (operation == "start") { p.Calibration.Clear(); p.Calibrating = true; p.CalibrationAt = clock.GetTimestamp(); p.CalibrationStatus = "collecting"; }
        else if (operation == "cancel") { p.Calibrating = false; p.Calibration.Clear(); }
    }
    private static int Median(IEnumerable<int> values) { var a = values.Order().ToArray(); return a[a.Length / 2]; }
}
