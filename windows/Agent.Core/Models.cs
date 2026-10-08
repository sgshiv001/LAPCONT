// LapCont — Core — Enrollment, configuration and verified state
// License: MIT
using System.Text.Json.Serialization;

namespace LapCont.Core;

[Flags]
public enum Grants { None = 0, Lock = 1, Proximity = 2, Camera = 4, Microphone = 8, Talk = 16 }

/// <summary>Persisted enrollment. The coordinator serializes access; secrets belong in protected storage.</summary>
public sealed record Enrollment(string PhoneId, string Name, string CertificatePin, string WindowsSid,
    Grants Permissions, byte[] ProximityKey, bool ProximityEnabled = false, int? CalibratedThreshold = null);

/// <summary>Validated owner settings. No credentials; changes are made locally by an authorized companion.</summary>
public sealed record Settings
{
    public int Port { get; init; } = 4433;
    public string? RelayUrl { get; init; }
    public int RssiAwayThreshold { get; init; } = -70;
    public int RssiReturnHysteresisDb { get; init; } = 6;
    public int LockDelaySeconds { get; init; } = 10;
    public int MissingBeaconGraceSeconds { get; init; } = 30;
    public bool ProximityPaused { get; init; } = true;
    public bool AllowMediaWhileLocked { get; init; }
    public string? SpeakerDevice { get; init; }
    public void Validate()
    {
        if (Port is < 1024 or > 65535 || RssiAwayThreshold is < -100 or > -30 || RssiReturnHysteresisDb is < 3 or > 20 ||
            LockDelaySeconds is < 10 or > 300 || MissingBeaconGraceSeconds is < 30 or > 300)
            throw new ArgumentException("Configuration outside supported ranges");
        if (RelayUrl is not null && (!Uri.TryCreate(RelayUrl, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)))
            throw new ArgumentException("Relay requires a wss URL without credentials or query");
    }
}

/// <summary>Only WTS observations change LockState; connection and capture state are independent.</summary>
public sealed record SessionState(int WindowsSessionId, string WindowsSid, string LockState = "unknown", bool AgentAvailable = false);

/// <summary>Metadata only. The bounded journal never holds captured media.</summary>
public sealed record PcEvent(string EventId, DateTimeOffset ObservedAtUtc, int WindowsSessionId,
    string WindowsSid, string Event, string? VerifiedUser, string? RequestId = null);

/// <summary>Typed business failure safe to show to the authenticated peer.</summary>
public sealed class CommandFailure(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
}

/// <summary>Wire JSON uses explicit snake_case for interoperability. Stateless and thread safe.</summary>
public static class Wire
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower, Converters = { new JsonStringEnumConverter() } };
    public static byte[] Encode(object value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Options);
}
