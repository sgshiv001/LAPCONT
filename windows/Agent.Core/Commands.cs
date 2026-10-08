// LapCont — Core — Strict LPC1 schema and bounded reconnect idempotency
// License: MIT
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LapCont.Protocol;

namespace LapCont.Core;

/// <summary>Validated envelope. Parsing is stateless; parameters are cloned and immutable.</summary>
public sealed record Command(string RequestId, string PcId, string PhoneId, string ConnectionSessionId,
    long Sequence, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string Name, JsonElement Params)
{
    public int SessionId => Params.GetProperty("windows_session_id").GetInt32();
    public string StreamId => Params.GetProperty("stream_id").GetString()!.ToLowerInvariant();
    public static bool HexId(string value) => value.Length == 32 && value.All(Uri.IsHexDigit);
    public static Command Parse(byte[] payload, string pc, string phone, string connection, DateTimeOffset now)
    {
        using var doc = Frames.ParseControl(payload); var root = doc.RootElement;
        Exact(root, "protocol", "kind", "request_id", "pc_id", "phone_id", "connection_session_id", "sequence", "issued_at_utc", "expires_at_utc", "command", "params");
        var name = Text(root, "command", 32);
        if (!Frames.Commands.Contains(name) || name == "pair") throw new CommandFailure("UNKNOWN_COMMAND", "Unsupported authenticated command");
        if (Text(root, "protocol", 4) != "LPC1" || Text(root, "kind", 16) != "command" ||
            Text(root, "pc_id", 64) != pc || Text(root, "phone_id", 64) != phone || Text(root, "connection_session_id", 32) != connection)
            throw new CommandFailure("IDENTITY_MISMATCH", "Connection context mismatch");
        var id = Text(root, "request_id", 32); if (!HexId(id)) throw new CommandFailure("INVALID_REQUEST", "Invalid request ID");
        var seq = root.GetProperty("sequence").GetInt64(); if (seq <= 0) throw new CommandFailure("REPLAY", "Invalid sequence");
        if (!DateTimeOffset.TryParse(Text(root, "issued_at_utc", 40), out var issued) ||
            !DateTimeOffset.TryParse(Text(root, "expires_at_utc", 40), out var expires) || issued.Offset != TimeSpan.Zero || expires.Offset != TimeSpan.Zero ||
            expires <= issued || expires - issued > TimeSpan.FromSeconds(10) || issued > now.AddSeconds(5))
            throw new CommandFailure("CLOCK_SKEW", "Refresh PC time and submit a new action");
        if (expires <= now) throw new CommandFailure("EXPIRED", "Action expired; submit a new action");
        var p = root.GetProperty("params");
        switch (name)
        {
            case "status": case "unpair": Exact(p); break;
            case "lock": case "unlock_request": Exact(p, "windows_session_id"); Session(p); break;
            case "stream_stop": case "talk_stop": Exact(p, "stream_id"); Stream(p); break;
            case "talk_start": Exact(p, "windows_session_id", "stream_id"); Session(p); Stream(p); break;
            case "calibrate":
                Exact(p, "windows_session_id", "operation"); Session(p);
                if (Text(p, "operation", 8) is not ("start" or "cancel" or "query")) throw new CommandFailure("INVALID_PARAMS", "Invalid calibration operation"); break;
            case "stream_start":
                Exact(p, "windows_session_id", "stream_id", "video", "audio", "width", "height", "fps", "bitrate", "recovery"); Session(p); Stream(p);
                var video = p.GetProperty("video").GetBoolean(); var audio = p.GetProperty("audio").GetBoolean(); p.GetProperty("recovery").GetBoolean();
                var w = p.GetProperty("width").GetInt32(); var h = p.GetProperty("height").GetInt32(); var b = p.GetProperty("bitrate").GetInt32();
                if ((!video && !audio) || p.GetProperty("fps").GetInt32() != 30 || !(w == 640 && h == 480 && b == 1000000 || w == 1280 && h == 720 && b == 2000000))
                    throw new CommandFailure("INVALID_PARAMS", "Use a supported 480p or 720p preset"); break;
        }
        return new(id, pc, phone, connection, seq, issued, expires, name, p.Clone());
    }
    public static void Exact(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != names.Length || root.EnumerateObject().Any(p => !names.Contains(p.Name)))
            throw new CommandFailure("INVALID_PARAMS", "Unexpected or missing fields");
    }
    public static string Text(JsonElement root, string name, int maximum)
    {
        var p = root.GetProperty(name);
        if (p.ValueKind != JsonValueKind.String || p.GetString() is not { Length: > 0 } s || s.Length > maximum || s.Any(char.IsControl))
            throw new CommandFailure("INVALID_PARAMS", "Invalid string field"); return s;
    }
    private static void Session(JsonElement p) { if (p.GetProperty("windows_session_id").GetInt32() <= 0) throw new CommandFailure("INVALID_PARAMS", "Interactive session required"); }
    private static void Stream(JsonElement p) { if (!HexId(Text(p, "stream_id", 32))) throw new CommandFailure("INVALID_PARAMS", "Invalid stream ID"); }
}

/// <summary>Coordinator-owned cache. Capacity rejects new actions instead of evicting retry protection.</summary>
public sealed class RequestLedger(TimeProvider clock, int capacity = 256)
{
    private readonly Dictionary<string, (string Hash, byte[]? Outcome, long At)> entries = new(StringComparer.Ordinal);
    public byte[]? Begin(Command command)
    {
        foreach (var old in entries.Where(p => clock.GetElapsedTime(p.Value.At) >= TimeSpan.FromMinutes(2)).Select(p => p.Key).ToArray()) entries.Remove(old);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Name + "\n" + command.Params.GetRawText())));
        if (entries.TryGetValue(command.RequestId, out var previous))
        {
            if (previous.Hash != hash) throw new CommandFailure("REQUEST_ID_REUSED", "Request ID belongs to different parameters");
            return previous.Outcome ?? throw new CommandFailure("REQUEST_PENDING", "This request is still pending");
        }
        if (entries.Count >= capacity) throw new CommandFailure("RATE_LIMITED", "Retry window is full");
        entries.Add(command.RequestId, (hash, null, clock.GetTimestamp())); return null;
    }
    public void Finish(string id, byte[] response) { var e = entries[id]; entries[id] = (e.Hash, response, e.At); }
}
