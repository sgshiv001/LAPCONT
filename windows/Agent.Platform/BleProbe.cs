// LapCont — Platform — Real WinRT adapter and app-beacon observation
// License: MIT
using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace LapCont.Platform;

/// <summary>Short foreground observer. Callbacks update a concurrent map; returned report contains no MAC addresses.</summary>
public static class BleProbe
{
    // 16-bit custom test service identifier in service-data AD type 0x16, 2 + 16 bytes = 18.
    // This is a Phase 0 test beacon, not an allocated production service or authenticated identity.
    public const ushort TestService = 0xFFF0;
    public static async Task<object> RunAsync(TimeSpan duration, CancellationToken ct)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(ct);
        if (adapter is null) return new { status = "BLE_UNAVAILABLE", detail = "No Bluetooth adapter" };
        var radio = await adapter.GetRadioAsync().AsTask(ct);
        if (radio.State != RadioState.On) return new { status = "BLE_UNAVAILABLE", detail = "Bluetooth radio is off" };
        var beacons = new ConcurrentDictionary<string, (short Rssi, long Count)>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
        string? stopError = null;
        void Received(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs e)
        {
            foreach (var section in e.Advertisement.DataSections.Where(s => s.DataType == 0x16))
            {
                using var reader = DataReader.FromBuffer(section.Data); var bytes = new byte[section.Data.Length]; reader.ReadBytes(bytes);
                if (bytes.Length != 18 || bytes[0] != 0xF0 || bytes[1] != 0xFF) continue;
                var id = Convert.ToHexString(bytes.AsSpan(2));
                beacons.AddOrUpdate(id, (e.RawSignalStrengthInDBm, 1), (_, value) => (e.RawSignalStrengthInDBm, value.Count + 1));
            }
        }
        watcher.Received += Received;
        watcher.Stopped += (_, e) => stopError = e.Error == BluetoothError.Success ? null : e.Error.ToString();
        try { watcher.Start(); await Task.Delay(duration, ct); }
        finally { watcher.Stop(); watcher.Received -= Received; }
        return new { status = stopError is null ? "observer_ran" : "observer_failed", error = stopError,
            adapter.IsLowEnergySupported, adapter.IsCentralRoleSupported,
            distinct_test_beacons = beacons.Count, observations = beacons.Values.Sum(v => v.Count),
            latest_rssi = beacons.Values.Select(v => v.Rssi).ToArray(), duration_seconds = duration.TotalSeconds };
    }
}
