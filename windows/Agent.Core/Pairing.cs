// LapCont — Core — Atomic one-use, expiring local enrollment
// License: MIT
using System.Security.Cryptography;

namespace LapCont.Core;

/// <summary>Thread-safe token reservation. A reserved token cannot authorize a concurrent enrollment.</summary>
public sealed class PairingWindow(TimeProvider clock)
{
    private readonly object gate = new();
    private byte[]? token;
    private long opened;
    private string? reservedPin;
    public string WindowsSid { get; private set; } = "";
    public int SessionId { get; private set; }
    public TaskCompletionSource<Grants> Confirmation { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Open(string sid, int session)
    {
        lock (gate)
        {
            Confirmation.TrySetCanceled(); Confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            token = RandomNumberGenerator.GetBytes(32); opened = clock.GetTimestamp(); reservedPin = null; WindowsSid = sid; SessionId = session;
            return Convert.ToHexString(token);
        }
    }
    public bool IsOpen { get { lock (gate) return token is not null && clock.GetElapsedTime(opened) < TimeSpan.FromSeconds(60); } }
    public void Reserve(string presented, string pin)
    {
        lock (gate)
        {
            byte[] decoded;
            try { decoded = Convert.FromHexString(presented); } catch (FormatException) { throw new CommandFailure("PAIRING_REJECTED", "Invalid pairing token"); }
            if (!IsOpen || reservedPin is not null || decoded.Length != 32 || !CryptographicOperations.FixedTimeEquals(token!, decoded))
                throw new CommandFailure("PAIRING_REJECTED", "Pairing expired, consumed or invalid");
            reservedPin = pin;
        }
    }
    public void Confirm(string sid, string pin, Grants grants)
    {
        lock (gate)
        {
            if (!IsOpen || sid != WindowsSid || pin != reservedPin || ((int)grants & ~31) != 0) throw new CommandFailure("PAIRING_REJECTED", "Confirmation does not match enrollment");
            token = null; Confirmation.TrySetResult(grants);
        }
    }
    public void Cancel() { lock (gate) { token = null; Confirmation.TrySetCanceled(); } }
}
