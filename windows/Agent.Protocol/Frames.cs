// LapCont — Protocol — Versioned bounded stream and media framing
// License: MIT
using System.Buffers.Binary;
using System.Text.Json;
using System.Text;

namespace LapCont.Protocol;

/// <summary>Immutable media header. Callers own payload memory; concurrent mutation is not supported.</summary>
public sealed record MediaFrame(byte Type, ushort Flags, byte[] StreamId, ulong Sequence, long PtsMicroseconds, byte[] Payload);

/// <summary>Stateless byte codecs used inside TLS. Thread safe; callers serialize writes to a stream.</summary>
public static class Frames
{
    public const int ControlLimit = 4096;
    public const int MediaLimit = 1048576;
    public const int MediaHeaderBytes = 40;
    public static readonly IReadOnlySet<string> Commands = new HashSet<string>(StringComparer.Ordinal)
    { "pair", "status", "lock", "unlock_request", "stream_start", "stream_stop", "talk_start", "talk_stop", "calibrate", "unpair" };

    public static byte[] EncodeMedia(MediaFrame frame)
    {
        if (frame.Type is < 1 or > 5 || frame.Flags > 3 || frame.StreamId.Length != 16 || frame.Sequence > long.MaxValue || frame.PtsMicroseconds < 0 || frame.Payload.Length > MediaLimit)
            throw new InvalidDataException("Invalid media header or length");
        var bytes = new byte[MediaHeaderBytes + frame.Payload.Length];
        bytes[0] = 1; bytes[1] = frame.Type;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), frame.Flags);
        frame.StreamId.CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(20), frame.Sequence);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(28), frame.PtsMicroseconds);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36), (uint)frame.Payload.Length);
        frame.Payload.CopyTo(bytes, MediaHeaderBytes);
        return bytes;
    }

    public static MediaFrame DecodeMedia(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MediaHeaderBytes || bytes[0] != 1 || bytes[1] is < 1 or > 5)
            throw new InvalidDataException("Invalid media version/type");
        var flags = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]);
        var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[36..]);
        var pts = BinaryPrimitives.ReadInt64BigEndian(bytes[28..]);
        if (flags > 3 || pts < 0 || BinaryPrimitives.ReadUInt64BigEndian(bytes[20..]) > long.MaxValue || length > MediaLimit || bytes.Length != MediaHeaderBytes + length)
            throw new InvalidDataException("Invalid media fields/length");
        return new(bytes[1], flags, bytes.Slice(4, 16).ToArray(), BinaryPrimitives.ReadUInt64BigEndian(bytes[20..]), pts, bytes[40..].ToArray());
    }

    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, int limit, CancellationToken ct)
    {
        if (payload.Length == 0 || payload.Length > limit) throw new InvalidDataException("Frame length out of bounds");
        var header = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(payload, ct); await stream.FlushAsync(ct);
    }
    public static async Task<byte[]> ReadAsync(Stream stream, int limit, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0 || length > limit) throw new InvalidDataException("Frame length out of bounds");
        var payload = new byte[(int)length]; await stream.ReadExactlyAsync(payload, ct); return payload;
    }

    public static JsonDocument ParseControl(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > ControlLimit || bytes.Length == 0) throw new InvalidDataException("Control length out of bounds");
        try { new UTF8Encoding(false, true).GetCharCount(bytes.Span); }
        catch (DecoderFallbackException e) { throw new JsonException("Invalid UTF-8", e); }
        var reader = new Utf8JsonReader(bytes.Span, new JsonReaderOptions { MaxDepth = 12 });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString()!))
                throw new JsonException("Duplicate property");
        }
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw new JsonException("Control root must be object"); }
        return document;
    }
}
