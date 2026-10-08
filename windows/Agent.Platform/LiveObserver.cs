// LapCont — Platform — Healthy BLE observer and finalized one-second RSSI buckets
// License: MIT
using System.Collections.Concurrent;
using LapCont.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace LapCont.Platform;

/// <summary>Worker-owned watcher. Callback only aggregates valid enrolled rotating identifiers; never exposes MAC addresses.</summary>
public static class LiveObserver
{
    public static async Task RunAsync(string pc, IReadOnlyDictionary<string, byte[]> phones, Action<bool> health, Action<string, int, long> bucket, CancellationToken ct)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(ct); if (adapter is null || !adapter.IsCentralRoleSupported) { health(false); return; }
        var radio = await adapter.GetRadioAsync().AsTask(ct); if (radio.State != RadioState.On) { health(false); return; }
        var values = new ConcurrentDictionary<(string Phone, long Second), ConcurrentBag<int>>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        void Received(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            foreach (var section in e.Advertisement.DataSections.Where(s => s.DataType == 0x16 && s.Data.Length == 18))
            {
                using var reader = DataReader.FromBuffer(section.Data); var bytes = new byte[18]; reader.ReadBytes(bytes);
                if (bytes[0] != 0xF0 || bytes[1] != 0xFF) continue; var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var (id, key) in phones)
                    if (Beacon.Matches(bytes[2..], key, pc, id, now)) { var second = System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency; var bag = values.GetOrAdd((id, second), _ => new()); if (bag.Count < 128) bag.Add(e.RawSignalStrengthInDBm); }
            }
        }
        watcher.Received += Received; watcher.Stopped += (_, _) => health(false);
        try
        {
            watcher.Start(); health(true);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct); if (radio.State != RadioState.On || watcher.Status != BluetoothLEAdvertisementWatcherStatus.Started) { health(false); return; }
                var second = System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
                foreach (var k in values.Keys.Where(k => k.Second < second).OrderBy(k => k.Second))
                    if (values.TryRemove(k, out var readings) && k.Second >= second - 2 && readings.Count > 0) { var sorted = readings.Order().ToArray(); bucket(k.Phone, sorted[sorted.Length / 2], k.Second * System.Diagnostics.Stopwatch.Frequency); }
            }
        }
        finally { watcher.Received -= Received; watcher.Stop(); health(false); }
    }
}
