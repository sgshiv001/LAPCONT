// LapCont — Tests — Pairing expiry/concurrency, protocol identity, idempotency and proximity timing
// License: MIT
using System.Text.Json;
using LapCont.Core;
using Xunit;
namespace LapCont.Tests;

public sealed class PolicyTests
{
    private sealed class Clock : TimeProvider
    {
        private long value;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => value;
        public void Advance(int milliseconds) => value += milliseconds;
    }
    [Fact] public void PairingExpiryAndOneUse()
    {
        var clock = new Clock(); var p = new PairingWindow(clock); var token = p.Open("sid", 1); clock.Advance(60000);
        Assert.Throws<CommandFailure>(() => p.Reserve(token, "pin")); token = p.Open("sid", 1); p.Reserve(token, "pin");
        Assert.Throws<CommandFailure>(() => p.Reserve(token, "pin2")); Assert.Throws<CommandFailure>(() => p.Confirm("other", "pin", Grants.Lock));
        p.Confirm("sid", "pin", Grants.Lock); Assert.False(p.IsOpen); Assert.Throws<CommandFailure>(() => p.Reserve(token, "pin"));
    }
    [Fact] public async Task ConcurrentPairingHasExactlyOneWinner()
    {
        var p = new PairingWindow(new Clock()); var token = p.Open("sid", 1); var winners = 0;
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() => { try { p.Reserve(token, i.ToString()); Interlocked.Increment(ref winners); } catch (CommandFailure) { } })));
        Assert.Equal(1, winners);
    }
    private static byte[] Payload(string name, object parameters, string phone = "phone", string connection = "connection", DateTimeOffset? expiry = null) => Wire.Encode(new {
        protocol = "LPC1", kind = "command", request_id = new string('a', 32), pc_id = "pc", phone_id = phone, connection_session_id = connection,
        sequence = 1, issued_at_utc = DateTimeOffset.UtcNow.AddSeconds(-1), expires_at_utc = expiry ?? DateTimeOffset.UtcNow.AddSeconds(5), command = name, @params = parameters });
    [Fact] public void ClaimedIdentityCannotCrossConnections()
    {
        Assert.Throws<CommandFailure>(() => Command.Parse(Payload("status", new { }, "other"), "pc", "phone", "connection", DateTimeOffset.UtcNow));
        Assert.Throws<CommandFailure>(() => Command.Parse(Payload("status", new { }, connection: "old"), "pc", "phone", "connection", DateTimeOffset.UtcNow));
    }
    [Fact] public void UnknownParametersAndExpiredRequestsFail()
    {
        Assert.Throws<CommandFailure>(() => Command.Parse(Payload("lock", new { windows_session_id = 1, shutdown = true }), "pc", "phone", "connection", DateTimeOffset.UtcNow));
        Assert.Throws<CommandFailure>(() => Command.Parse(Payload("status", new { }, expiry: DateTimeOffset.UtcNow.AddSeconds(-.5)), "pc", "phone", "connection", DateTimeOffset.UtcNow));
    }
    [Fact] public void LedgerDoesNotRepeatSideEffectsOrEvictLiveProtection()
    {
        var clock = new Clock(); var cache = new RequestLedger(clock, 1); var c = Command.Parse(Payload("lock", new { windows_session_id = 1 }), "pc", "phone", "connection", DateTimeOffset.UtcNow);
        Assert.Null(cache.Begin(c)); Assert.Throws<CommandFailure>(() => cache.Begin(c)); cache.Finish(c.RequestId, new byte[] { 1 }); Assert.Equal(new byte[] { 1 }, cache.Begin(c with { ConnectionSessionId = "new" }));
        Assert.Throws<CommandFailure>(() => cache.Begin(c with { Params = JsonSerializer.SerializeToElement(new { windows_session_id = 2 }) }));
        Assert.Throws<CommandFailure>(() => cache.Begin(c with { RequestId = new string('b', 32) })); clock.Advance(120000); Assert.Null(cache.Begin(c with { RequestId = new string('b', 32) }));
    }
    [Fact] public void RotatingBeaconChecksKeyPeerAndWindow()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(); var bytes = Beacon.Create(key, "pc", "phone", 1800);
        Assert.True(Beacon.Matches(bytes, key, "pc", "phone", 1829)); Assert.False(Beacon.Matches(bytes, key, "other", "phone", 1829)); Assert.False(Beacon.Matches(bytes, key, "pc", "phone", 1900));
        bytes[10] ^= 1; Assert.False(Beacon.Matches(bytes, key, "pc", "phone", 1800));
    }
    [Fact] public void StartupAndFailureNeverBecomeFabricatedAway()
    {
        var clock = new Clock(); var p = new ProximityPolicy(clock, new Settings { ProximityPaused = false }); p.Configure(new[] { "a" }); p.Health(true); clock.Advance(60000); Assert.False(p.Tick());
        for (var i = 0; i < 20; i++) { p.Bucket("a", -90); clock.Advance(1000); } Assert.False(p.Tick());
        p.RestoreCalibration("a", -70); for (var i = 0; i < 7; i++) { p.Bucket("a", -45); clock.Advance(1000); }
        for (var i = 0; i < 18; i++) { p.Bucket("a", -90); clock.Advance(1000); } Assert.True(p.Tick()); p.Health(false); clock.Advance(60000); Assert.False(p.Tick());
    }
    [Fact] public void AllEligiblePhonesMustBeAwayAndGraceRequiresArming()
    {
        var clock = new Clock(); var p = new ProximityPolicy(clock, new Settings { ProximityPaused = false }); p.Configure(new[] { "a", "b" }); p.Health(true);
        p.RestoreCalibration("a", -70); p.RestoreCalibration("b", -70);
        for (var i = 0; i < 7; i++) { p.Bucket("a", -45); p.Bucket("b", -45); clock.Advance(1000); }
        for (var i = 0; i < 18; i++) { p.Bucket("a", -90); p.Bucket("b", -45); clock.Advance(1000); }
        clock.Advance(10000); Assert.False(p.Tick()); clock.Advance(21000); Assert.True(p.Tick());
        p.Configure(Array.Empty<string>()); Assert.False(p.Tick());
    }
    [Fact] public void CalibrationNeedsEightDistinctValidBuckets()
    {
        var clock = new Clock(); var p = new ProximityPolicy(clock, new Settings()); p.Configure(new[] { "a" }); p.Health(true); p.Calibrate("a", "start");
        for (var i = 0; i < 100; i++) p.Bucket("a", -50);
        Assert.Contains("\"calibration_samples\":1", System.Text.Encoding.UTF8.GetString(Wire.Encode(p.Status("a"))));
        for (var i = 0; i < 7; i++) { clock.Advance(1000); p.Bucket("a", -50); }
        Assert.Contains("\"threshold\":-62", System.Text.Encoding.UTF8.GetString(Wire.Encode(p.Status("a"))));
    }
    [Fact] public void GapsAndStaleIpcBucketsCannotCompleteWeakSignalDelay()
    {
        var clock=new Clock(); var p=new ProximityPolicy(clock,new Settings { ProximityPaused=false }); p.Configure(new[]{"a"}); p.Health(true); p.RestoreCalibration("a",-70);
        for(var i=0;i<7;i++) { p.Bucket("a",-45); clock.Advance(1000); }
        for(var i=0;i<9;i++) { p.Bucket("a",-90); clock.Advance(1000); }
        clock.Advance(4000); p.Bucket("a",-90,clock.GetTimestamp()-4000); Assert.False(p.Tick());
        for(var i=0;i<7;i++) { p.Bucket("a",-90); clock.Advance(1000); Assert.False(p.Tick()); }
    }
    [Fact] public void InsufficientCalibrationAndResumeRequireFreshEvidence()
    {
        var clock=new Clock(); var p=new ProximityPolicy(clock,new Settings { ProximityPaused=false }); p.Configure(new[]{"a"}); p.Health(true); p.Calibrate("a","start");
        p.Bucket("a",-50); clock.Advance(21000); Assert.False(p.Tick());
        Assert.Contains("insufficient_data",System.Text.Encoding.UTF8.GetString(Wire.Encode(p.Status("a"))));
        p.RestoreCalibration("a",-70); for(var i=0;i<7;i++) { p.Bucket("a",-45); clock.Advance(1000); }
        p.Health(false); p.Health(true); clock.Advance(60000); Assert.False(p.Tick()); Assert.Empty(p.Returned());
    }
    [Fact] public void ReturnNeedsStableFreshNearAndIsDeduplicated()
    {
        var clock=new Clock(); var p=new ProximityPolicy(clock,new Settings { ProximityPaused=false }); p.Configure(new[]{"a"}); p.Health(true); p.RestoreCalibration("a",-70);
        for(var i=0;i<7;i++) { p.Bucket("a",-45); clock.Advance(1000); }
        clock.Advance(31000); Assert.True(p.Tick());
        for(var i=0;i<7;i++) { p.Bucket("a",-45); clock.Advance(1000); p.Tick(); Assert.Empty(p.Returned()); }
        for(var i=0;i<3;i++) { p.Bucket("a",-45); clock.Advance(1000); p.Tick(); }
        Assert.Equal(new[]{"a"},p.Returned()); Assert.Empty(p.Returned());
        clock.Advance(31000); Assert.True(p.Tick());
        for(var i=0;i<10;i++) { p.Bucket("a",-45); clock.Advance(1000); p.Tick(); }
        Assert.Empty(p.Returned()); // Cooldown survives a second return transition.
    }
}
