// LapCont — Tests — Malformed wire input and cross-language media vector
// License: MIT
using System.Text;
using System.Text.Json;
using LapCont.Protocol;
using Xunit;

namespace LapCont.Tests;

/// <summary>Isolated deterministic format tests; shared immutable vector is also checked by Kotlin.</summary>
public sealed class FramingTests
{
    public const string Vector = "01010001000102030405060708090A0B0C0D0E0F000000000000002A000000000000823500000006000000016588";
    [Fact] public void SharedMediaVector()
    {
        var frame = new MediaFrame(1, 1, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(), 42, 33333, Convert.FromHexString("000000016588"));
        Assert.Equal(Vector, Convert.ToHexString(Frames.EncodeMedia(frame)));
        var decoded = Frames.DecodeMedia(Convert.FromHexString(Vector));
        Assert.Equal(frame.Payload, decoded.Payload); Assert.Equal(frame.PtsMicroseconds, decoded.PtsMicroseconds);
    }
    [Theory] [InlineData("{\"a\":1,\"a\":2}")] [InlineData("{\"nested\":{\"a\":1,\"a\":2}}")] [InlineData("[]")]
    public void InvalidControl(string json) => Assert.Throws<JsonException>(() => Frames.ParseControl(Encoding.UTF8.GetBytes(json)));
    [Fact] public void InvalidUtf8() => Assert.Throws<JsonException>(() => Frames.ParseControl(new byte[] { 123, 34, 120, 34, 58, 34, 255, 34, 125 }));
    [Fact] public void ExcessiveNesting() => Assert.ThrowsAny<JsonException>(() => Frames.ParseControl(Encoding.UTF8.GetBytes(new string('[', 14) + "0" + new string(']', 14))));
    [Fact] public void OversizedMediaRejectedBeforeAllocation()
    {
        var bytes = Convert.FromHexString(Vector); bytes[36] = 0x7f;
        Assert.Throws<InvalidDataException>(() => Frames.DecodeMedia(bytes));
        Assert.Throws<InvalidDataException>(() => Frames.DecodeMedia(bytes[..20]));
    }
    [Fact] public async Task PartialStreamAndLengthLimits()
    {
        using var stream = new MemoryStream();
        await Frames.WriteAsync(stream, Encoding.UTF8.GetBytes("verified"), 4096, default); stream.Position = 0;
        Assert.Equal("verified", Encoding.UTF8.GetString(await Frames.ReadAsync(stream, 4096, default)));
        using var bad = new MemoryStream(Convert.FromHexString("FFFFFFFF"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Frames.ReadAsync(bad, 4096, default));
        using var truncated = new MemoryStream(Convert.FromHexString("0000000401"));
        await Assert.ThrowsAsync<EndOfStreamException>(() => Frames.ReadAsync(truncated, 4096, default));
    }
    [Fact] public void TenBaselineCommands() { Assert.Equal(10, Frames.Commands.Count); Assert.DoesNotContain("unlock", Frames.Commands); Assert.DoesNotContain("exec", Frames.Commands); }
}
