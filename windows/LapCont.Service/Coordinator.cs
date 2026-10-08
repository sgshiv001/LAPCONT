// LapCont — Service — Enrollment, scoped commands, authoritative events and media leases
// License: MIT
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using LapCont.Core;
using LapCont.Platform;
using LapCont.Protocol;
using LapCont.Security;

namespace LapCont.Service;

/// <summary>Serialized coordinator state. Every network action rechecks enrollment and SID; no arbitrary execution.</summary>
public sealed class Coordinator : IAsyncDisposable
{
    private sealed record Registry(string PcId, List<Enrollment> Phones, Settings Settings);
    private sealed record StreamOwner(string Phone, string Connection, int Session, string Id, string Parameters, bool Talk, long Renewed);
    private readonly SemaphoreSlim gate = new(1);
    private readonly ProtectedStore store;
    private readonly CancellationTokenSource life = new();
    private readonly Dictionary<int, SessionLink> links = new();
    private readonly Dictionary<int, SessionState> states = new();
    private readonly Dictionary<string, RequestLedger> ledgers = new();
    private readonly Dictionary<int, StreamOwner> streams = new();
    private readonly Dictionary<int, StreamOwner> talks = new();
    private readonly Dictionary<(string Phone,string Stream),long> endedStreams = new();
    private readonly Queue<PcEvent> events = new();
    private readonly Dictionary<(int, string), long> eventDedup = new();
    private readonly ConcurrentDictionary<string, PeerConnection> connections = new();
    private readonly Dictionary<string, (string Connection, long At)> attachments = new();
    private readonly Dictionary<int, ProximityPolicy> proximity = new();
    private Registry registry;
    private readonly Dictionary<string, Settings> ownerSettings;
    private Enrollment[] enrollmentSnapshot = Array.Empty<Enrollment>();
    private readonly Dictionary<int, (string Phone, string Request, long At)> pendingLocks = new();
    private readonly List<RelayConnector.Route> routes;
    private readonly Dictionary<string, (CancellationTokenSource Life, Task Job)> relayJobs = new();
    private readonly List<RelayConnector.Route> pendingRelayRevocations;
    public Func<string, string, System.Net.WebSockets.WebSocket, byte[], CancellationToken, Task>? RelayEndpoint { get; set; }
    public string PcId => registry.PcId;
    public Settings Settings => registry.Settings;
    public PairingWindow Pairing { get; } = new(TimeProvider.System);
    public System.Security.Cryptography.X509Certificates.X509Certificate2 Identity { get; }
    public string Pin => LapCont.Security.Identity.Fingerprint(Identity);
    public Coordinator(ProtectedStore store)
    {
        this.store = store; var initial = InitialSettings(); registry = store.Load<Registry>("registry") ?? new(Guid.NewGuid().ToString("N"), new(), initial);
        registry = registry with { Settings = initial }; ownerSettings = store.Load<Dictionary<string, Settings>>("owner-settings") ?? new();
        registry.Settings.Validate(); Identity = store.Identity(); Save();
        foreach (var e in (store.Load<PcEvent[]>("events") ?? Array.Empty<PcEvent>()).Where(e => e.ObservedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5)).TakeLast(128)) events.Enqueue(e);
        routes = store.Load<List<RelayConnector.Route>>("relay-routes") ?? new(); pendingRelayRevocations = store.Load<List<RelayConnector.Route>>("relay-revocations") ?? new();
    }
    public void Log(string detail)
    {
        lock (store)
        {
            var path = Path.Combine(store.Folder, $"diagnostics-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.Move(path, path + ".1", true);
            File.AppendAllText(path, JsonSerializer.Serialize(new { at_utc = DateTimeOffset.UtcNow, pc_id = PcId, detail }) + Environment.NewLine);
            foreach (var p in Directory.GetFiles(store.Folder, "diagnostics-*.jsonl*").OrderByDescending(File.GetLastWriteTimeUtc).Skip(14)) File.Delete(p);
        }
    }
    private void Save() { store.Save("registry", registry); Volatile.Write(ref enrollmentSnapshot, registry.Phones.ToArray()); }
    private static Settings InitialSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory,"appsettings.json"); if(!File.Exists(path)) return new();
        if(new FileInfo(path).Length>65536) throw new InvalidDataException("Configuration too large");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path),new JsonDocumentOptions { MaxDepth=8 });
        var options = new JsonSerializerOptions(Wire.Options) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        return doc.RootElement.GetProperty("LapCont").Deserialize<Settings>(options) ?? throw new InvalidDataException("Settings required");
    }
    private Settings OwnerSettings(string sid) => ownerSettings.TryGetValue(sid,out var settings) ? settings with { Port=Settings.Port, RelayUrl=Settings.RelayUrl } : Settings;
    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, life.Token); var token = linked.Token;
        while (!token.IsCancellationRequested)
        {
            await gate.WaitAsync(token);
            try
            {
                // Console is a development mode. SCM obtains actual logged-on SID using WTSQueryUserToken.
                var sessions = Environment.UserInteractive ? new[] { WindowsSessions.CurrentSessionId } : WindowsSessions.EnumerateSessions();
                foreach (var id in sessions.Where(id => id > 0))
                {
                    var sid = CoordinatorPipe.SessionSid(id); if (sid is null) continue;
                    if (links.TryGetValue(id, out var prior) && prior.Sid == sid && prior.Available) continue;
                    if (links.ContainsKey(id)) continue; // Disconnect callback removes it; never create duplicate first-instance servers.
                    states.TryAdd(id, new(id, sid, WindowsSessions.LockState(id)));
                    var link = new SessionLink(id, sid, LocalAsync, MediaFromCompanionAsync, Availability, Log, token); links[id] = link; link.Start();
                }
                foreach (var owner in streams.Values.Concat(talks.Values).Where(s => TimeProvider.System.GetElapsedTime(s.Renewed) > TimeSpan.FromSeconds(s.Talk ? 5 : 30)).ToArray()) await StopAsync(owner, token);
                MaintainRelay(token);
                foreach(var key in endedStreams.Where(p=>TimeProvider.System.GetElapsedTime(p.Value)>=TimeSpan.FromMinutes(10)).Select(p=>p.Key).ToArray()) endedStreams.Remove(key);
                foreach (var (session, pending) in pendingLocks.ToArray()) if (TimeProvider.System.GetElapsedTime(pending.At) >= TimeSpan.FromSeconds(10))
                {
                    pendingLocks.Remove(session); if(pending.Request.Length!=32) continue;
                    var failed = Response(pending.Request, "failed", "LOCK_NOT_CONFIRMED", "Windows did not confirm the requested lock");
                    if (ledgers.TryGetValue(pending.Phone, out var ledger)) ledger.Finish(pending.Request, failed);
                    foreach (var peer in connections.Values.Where(p => p.PhoneId == pending.Phone)) peer.Send(failed);
                }
                foreach (var (id, policy) in proximity)
                {
                    var allAway = policy.Tick();
                    foreach (var returned in policy.Returned()) if (states.TryGetValue(id, out var locked) && locked.LockState == "locked")
                    {
                        var e = new PcEvent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, id, locked.WindowsSid, "PhoneReturned", null);
                        foreach (var peer in connections.Values.Where(c => c.PhoneId == returned)) peer.Send(Wire.Encode(new { protocol = "LPC1", kind = "event", pc_id = PcId, @event = e }));
                    }
                    if (allAway && !pendingLocks.ContainsKey(id) && states.TryGetValue(id, out var state) && state.LockState == "unlocked" && links.TryGetValue(id, out var agent) && agent.Available)
                    {
                        try { await agent.CallAsync("lock", new { }, token); pendingLocks[id] = ("", "", TimeProvider.System.GetTimestamp()); }
                        catch (CommandFailure e) { Log($"proximity lock session={id} code={e.Code}"); }
                    }
                }
            }
            finally { gate.Release(); }
            await Task.Delay(1000, token);
        }
    }
    private void Availability(SessionLink link, bool available)
    {
        _ = Task.Run(async () =>
        {
            await gate.WaitAsync();
            try
            {
                if (states.TryGetValue(link.SessionId, out var s)) states[link.SessionId] = s with { AgentAvailable = available };
                if (!available) { if (links.TryGetValue(link.SessionId, out var current) && ReferenceEquals(current, link)) links.Remove(link.SessionId);
                    EndSessionStreams(link.SessionId); if (proximity.TryGetValue(link.SessionId, out var p)) p.Health(false); }
                else UpdateObserver(link);
            }
            catch (Exception e) { Log($"companion availability error={e.GetType().Name}"); }
            finally { gate.Release(); }
        });
    }
    private void UpdateObserver(SessionLink link)
    {
        var eligible = registry.Phones.Where(p => p.WindowsSid == link.Sid && p.ProximityEnabled && p.Permissions.HasFlag(Grants.Proximity)).ToArray();
        if (!proximity.TryGetValue(link.SessionId, out var policy)) proximity[link.SessionId] = policy = new(TimeProvider.System, OwnerSettings(link.Sid));
        policy.Configure(eligible.Select(p => p.PhoneId));
        foreach (var p in eligible) if (p.CalibratedThreshold.HasValue) policy.RestoreCalibration(p.PhoneId, p.CalibratedThreshold.Value);
        link.Notify(new { kind = "observer_config", pc_id = PcId, paused = OwnerSettings(link.Sid).ProximityPaused,
            phones = eligible.Select(p => new { phone_id = p.PhoneId, key = Convert.ToHexString(p.ProximityKey) }) });
    }
    private async Task<object> LocalAsync(SessionLink link, JsonElement request)
    {
        await gate.WaitAsync(life.Token);
        try
        {
            var op = request.GetProperty("operation").GetString(); var p = request.GetProperty("params");
            switch (op)
            {
                case "open_pair":
                    var token = Pairing.Open(link.Sid, link.SessionId);
                    return new { ok = true, qr = new { app = "LapCont", v = 1, pc_id = PcId, pc_name = Environment.MachineName,
                        addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses)
                            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).Distinct().Take(8).ToArray(),
                        port = Settings.Port, relay_url = Settings.RelayUrl, cert_sha256 = Pin, identity_fingerprint = Pin, pairing_token = token, expires_in_seconds = 60 } };
                case "confirm_pair":
                    Pairing.Confirm(link.Sid, Command.Text(p, "pin", 64), (Grants)p.GetProperty("grants").GetInt32()); return new { ok = true };
                case "cancel_pair": Pairing.Cancel(); return new { ok = true };
                case "devices": return new { ok = true, devices = registry.Phones.Where(e => e.WindowsSid == link.Sid).Select(e => new { e.PhoneId, e.Name, pin = e.CertificatePin, grants = (int)e.Permissions, e.ProximityEnabled }), settings = OwnerSettings(link.Sid) };
                case "permissions":
                    var id = Command.Text(p, "phone_id", 64); var grants = p.GetProperty("grants").GetInt32();
                    if ((grants & ~31) != 0) throw new CommandFailure("INVALID_PARAMS", "Invalid grants");
                    var index = registry.Phones.FindIndex(e => e.PhoneId == id && e.WindowsSid == link.Sid);
                    if (index < 0) throw new CommandFailure("NOT_PAIRED", "Phone not enrolled for this Windows user");
                    registry.Phones[index] = registry.Phones[index] with { Permissions = (Grants)grants, ProximityEnabled = p.GetProperty("proximity_enabled").GetBoolean() && (grants & 2) != 0 };
                    Save(); foreach (var c in connections.Values.Where(c => c.PhoneId == id)) c.Close();
                    await StopPhoneAsync(id, life.Token); UpdateObserver(link); return new { ok = true };
                case "revoke":
                    await RevokeAsync(Command.Text(p, "phone_id", 64), link.Sid, life.Token); return new { ok = true };
                case "relay_route":
                    var routePhone = Command.Text(p, "phone_id", 32);
                    if (!registry.Phones.Any(f => f.PhoneId == routePhone && f.WindowsSid == link.Sid)) throw new CommandFailure("NOT_PAIRED", "Phone belongs to another Windows user");
                    var url = Command.Text(p, "url", 256); var credential = Command.Text(p, "agent_credential", 512);
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || uri.UserInfo != "" || uri.Query != "" || credential.Length < 32) throw new CommandFailure("INVALID_PARAMS", "Use a WSS URL and independent agent credential");
                    routes.RemoveAll(r => r.PhoneId == routePhone); routes.Add(new(routePhone, url, credential)); store.Save("relay-routes", routes);
                    if (relayJobs.Remove(routePhone, out var oldRelay)) oldRelay.Life.Cancel(); return new { ok = true };
                case "settings":
                    var previousOwnerSettings=OwnerSettings(link.Sid);
                    var updated = p.Deserialize<Settings>(Wire.Options) ?? throw new CommandFailure("INVALID_PARAMS", "Settings required"); updated.Validate();
                    // Listener/relay topology is administrator configuration, not a user-session privilege.
                    if (updated.Port != Settings.Port || updated.RelayUrl != Settings.RelayUrl) throw new CommandFailure("ADMIN_CONFIGURATION_REQUIRED", "Set listener and relay topology through protected administrator configuration");
                    ownerSettings[link.Sid] = updated; store.Save("owner-settings",ownerSettings);
                    if (updated.RssiAwayThreshold!=previousOwnerSettings.RssiAwayThreshold) {
                        for(var i=0;i<registry.Phones.Count;i++) if(registry.Phones[i].WindowsSid==link.Sid && registry.Phones[i].CalibratedThreshold.HasValue)
                            registry.Phones[i]=registry.Phones[i] with { CalibratedThreshold=updated.RssiAwayThreshold };
                        Save();
                    }
                    foreach (var l in links.Values.Where(l => l.Sid==link.Sid && l.Available)) {
                        proximity.Remove(l.SessionId); UpdateObserver(l);
                        if (!updated.AllowMediaWhileLocked && states.TryGetValue(l.SessionId,out var current) && current.LockState != "unlocked")
                            foreach (var owner in streams.Values.Concat(talks.Values).Where(o=>o.Session==l.SessionId).ToArray()) await StopAsync(owner,life.Token);
                    } return new { ok = true };
                case "observer_health": if (proximity.TryGetValue(link.SessionId, out var policy)) policy.Health(p.GetProperty("healthy").GetBoolean()); return new { ok = true };
                case "rssi":
                    if (proximity.TryGetValue(link.SessionId, out var prox))
                    {
                        var beaconPhone = Command.Text(p, "phone_id", 64); prox.Bucket(beaconPhone, p.GetProperty("rssi").GetInt32(), p.GetProperty("observed_at").GetInt64());
                        var beaconIndex = registry.Phones.FindIndex(f => f.PhoneId == beaconPhone && f.WindowsSid == link.Sid);
                        if (beaconIndex >= 0 && prox.Threshold(beaconPhone) is { } threshold && registry.Phones[beaconIndex].CalibratedThreshold != threshold) { registry.Phones[beaconIndex] = registry.Phones[beaconIndex] with { CalibratedThreshold = threshold }; Save(); }
                    }
                    return new { ok = true };
                case "local_stop":
                    EndSessionStreams(link.SessionId); return new { ok = true };
                case "capture_failed":
                    EndSessionStreams(link.SessionId); Log($"capture failed session={link.SessionId} code={Command.Text(p, "code", 64)}"); return new { ok = true };
                default: throw new CommandFailure("INVALID_PARAMS", "Unknown local operation");
            }
        }
        finally { gate.Release(); }
    }
    public Enrollment? FindByPin(string pin) => Volatile.Read(ref enrollmentSnapshot).FirstOrDefault(p => p.CertificatePin == pin);
    public async Task<object> EnrollAsync(string pin, JsonElement request, CancellationToken ct)
    {
        Command.Exact(request, "protocol", "kind", "command", "pairing_token", "phone_id", "phone_name");
        if (Command.Text(request, "protocol", 4) != "LPC1" || Command.Text(request, "kind", 16) != "command" || Command.Text(request, "command", 8) != "pair") throw new CommandFailure("PAIRING_REJECTED", "Invalid bootstrap message");
        var phone = Command.Text(request, "phone_id", 64); var name = Command.Text(request, "phone_name", 64); if (!Command.HexId(phone)) throw new CommandFailure("PAIRING_REJECTED", "Invalid phone ID");
        Task<Grants> confirmation; string sid;
        await gate.WaitAsync(ct);
        try
        {
            if (registry.Phones.Count >= 16 || registry.Phones.Any(p => p.PhoneId == phone || p.CertificatePin == pin)) throw new CommandFailure("PAIRING_REJECTED", "Existing or maximum enrollment reached");
            Pairing.Reserve(Command.Text(request, "pairing_token", 64), pin); sid = Pairing.WindowsSid; confirmation = Pairing.Confirmation.Task;
            if (!links.TryGetValue(Pairing.SessionId, out var link) || !link.Available) throw new CommandFailure("SESSION_AGENT_UNAVAILABLE", "Local confirmation is unavailable");
            link.Notify(new { kind = "pairing_pending", phone_id = phone, phone_name = name, pin });
        }
        finally { gate.Release(); }
        var grants = await confirmation.WaitAsync(TimeSpan.FromSeconds(60), ct);
        await gate.WaitAsync(ct);
        try
        {
            var enrollment = new Enrollment(phone, name, pin, sid, grants, RandomNumberGenerator.GetBytes(32)); registry.Phones.Add(enrollment); Save();
            return new { protocol = "LPC1", kind = "pair_result", ok = true, pc_id = PcId, phone_id = phone, pc_name = Environment.MachineName,
                windows_sid = sid, grants = (int)grants, proximity_key = Convert.ToHexString(enrollment.ProximityKey) };
        }
        finally { gate.Release(); }
    }
    public async Task RegisterAsync(PeerConnection connection, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            foreach (var old in connections.Values.Where(c => c.PhoneId == connection.PhoneId)) old.Close();
            connections[connection.Id] = connection; attachments[connection.Attachment] = (connection.Id, TimeProvider.System.GetTimestamp());
        }
        finally { gate.Release(); }
    }
    public async Task<PeerConnection> AttachAsync(string token, string phone, string connection, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!attachments.Remove(token, out var a) || TimeProvider.System.GetElapsedTime(a.At) > TimeSpan.FromSeconds(10) || a.Connection != connection ||
                !connections.TryGetValue(connection, out var c) || c.PhoneId != phone || !c.Alive || c.Media is not null) throw new CommandFailure("MEDIA_ATTACHMENT_REJECTED", "Media requires a fresh control attachment");
            return c;
        }
        finally { gate.Release(); }
    }
    public async Task DisconnectedAsync(PeerConnection connection)
    {
        await gate.WaitAsync();
        try { connections.TryRemove(connection.Id, out _); attachments.Remove(connection.Attachment); await StopConnectionAsync(connection.Id, life.Token); }
        finally { gate.Release(); }
    }
    private object Status(Enrollment phone) => new { pc_id = PcId, pc_name = Environment.MachineName, online = true, observed_at_utc = DateTimeOffset.UtcNow,
        grants = (int)phone.Permissions, true_unlock_available = false, sessions = states.Values.Where(s => s.WindowsSid == phone.WindowsSid).OrderByDescending(s => s.AgentAvailable).Take(2).Select(s => new {
            s.WindowsSessionId, s.LockState, s.AgentAvailable, media_state = streams.ContainsKey(s.WindowsSessionId) ? "active" : "idle", talk_state = talks.ContainsKey(s.WindowsSessionId) ? "active" : "idle",
            proximity = proximity.TryGetValue(s.WindowsSessionId, out var p) ? p.Status(phone.PhoneId) : new { state = "unknown" } }),
        events = events.Where(e => e.WindowsSid == phone.WindowsSid && e.ObservedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5)).TakeLast(3).ToArray() };
    public async Task<byte[]> ExecuteAsync(PeerConnection peer, Command c, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var phone = registry.Phones.FirstOrDefault(p => p.PhoneId == peer.PhoneId && p.CertificatePin == peer.Pin) ?? throw new CommandFailure("REVOKED", "Enrollment revoked");
            if (!ledgers.TryGetValue(phone.PhoneId, out var ledger)) ledgers[phone.PhoneId] = ledger = new(TimeProvider.System);
            var prior = ledger.Begin(c); if (prior is not null) return prior;
            byte[] result;
            try
            {
                string disposition = "completed", code = "OK", detail = "Completed";
                if (c.Name == "status") { }
                else if (c.Name == "unpair") await RevokeAsync(phone.PhoneId, phone.WindowsSid, ct, peer);
                else if (c.Name is "stream_stop" or "talk_stop")
                {
                    var map = c.Name == "stream_stop" ? streams : talks;
                    foreach (var o in map.Values.Where(o => o.Id == c.StreamId).ToArray())
                    { if (o.Phone != phone.PhoneId) throw new CommandFailure("NOT_STREAM_OWNER", "Stream belongs to another phone"); await StopAsync(o, ct); }
                    // A canceled in-flight Start can arrive after Stop. Retire even a not-yet-active ID.
                    if (endedStreams.Count<4096 || endedStreams.ContainsKey((phone.PhoneId,c.StreamId))) endedStreams[(phone.PhoneId,c.StreamId)] = TimeProvider.System.GetTimestamp();
                }
                else
                {
                    if (!states.TryGetValue(c.SessionId, out var state) || state.WindowsSid != phone.WindowsSid || CoordinatorPipe.SessionSid(c.SessionId) != phone.WindowsSid)
                        throw new CommandFailure("SESSION_SCOPE_DENIED", "Windows session is outside this pairing's scope");
                    if (!links.TryGetValue(c.SessionId, out var agent) || !agent.Available) throw new CommandFailure("SESSION_AGENT_UNAVAILABLE", "Open the PC companion");
                    switch (c.Name)
                    {
                        case "lock":
                            Require(phone, Grants.Lock); if (state.LockState != "locked")
                            {
                                if (pendingLocks.ContainsKey(c.SessionId)) throw new CommandFailure("LOCK_PENDING", "A lock request is awaiting Windows confirmation");
                                await agent.CallAsync("lock", new { request_id = c.RequestId }, ct); pendingLocks[c.SessionId] = (phone.PhoneId, c.RequestId, TimeProvider.System.GetTimestamp());
                                disposition = "accepted"; detail = "Waiting for the matching Windows lock event";
                            } break;
                        case "unlock_request":
                            Require(phone, Grants.Lock); await agent.CallAsync("unlock_request", new { }, ct); code = "SIGN_IN_REQUIRED"; detail = "Sign in at your PC. Windows authentication is still required."; break;
                        case "calibrate":
                            Require(phone, Grants.Proximity); if (!proximity.TryGetValue(c.SessionId, out var policy)) throw new CommandFailure("PROXIMITY_UNAVAILABLE", "Observer unavailable");
                            policy.Calibrate(phone.PhoneId, c.Params.GetProperty("operation").GetString()!); break;
                        case "stream_start": case "talk_start":
                            var talk = c.Name == "talk_start";
                            if (talk) Require(phone, Grants.Talk);
                            else { if (c.Params.GetProperty("video").GetBoolean()) Require(phone, Grants.Camera); if (c.Params.GetProperty("audio").GetBoolean()) Require(phone, Grants.Microphone); }
                            if (state.LockState != "unlocked" && !OwnerSettings(phone.WindowsSid).AllowMediaWhileLocked) throw new CommandFailure("MEDIA_LOCK_POLICY", "Media requires a verified unlocked Windows session");
                            if (peer.Media is null) throw new CommandFailure("MEDIA_UNAVAILABLE", "Connect the separate media channel first");
                            var map = talk ? talks : streams; var normalized = System.Text.Json.Nodes.JsonNode.Parse(c.Params.GetRawText())!;
                            if (!talk) normalized["recovery"] = false;
                            var parameters = normalized.ToJsonString();
                            if (map.TryGetValue(c.SessionId, out var existing))
                            {
                                if (existing.Phone != phone.PhoneId || existing.Connection != peer.Id || existing.Id != c.StreamId || existing.Parameters != parameters)
                                    throw new CommandFailure("MEDIA_BUSY", "An active stream owns this device; stop it before changing tracks or quality");
                                map[c.SessionId] = existing with { Renewed = TimeProvider.System.GetTimestamp() };
                                await agent.CallAsync(talk ? "talk_renew" : "stream_renew", new { stream_id = c.StreamId, recovery = !talk && c.Params.GetProperty("recovery").GetBoolean() }, ct);
                            }
                            else
                            {
                                if(endedStreams.ContainsKey((phone.PhoneId,c.StreamId))) throw new CommandFailure("STREAM_ENDED","This lease ended. Start a new stream with a fresh ID.");
                                if(endedStreams.Count>=4096) throw new CommandFailure("RATE_LIMITED","Too many recent media sessions");
                                await agent.CallAsync(c.Name, new { stream_id = c.StreamId, settings = c.Params, speaker_device = OwnerSettings(phone.WindowsSid).SpeakerDevice }, ct);
                                map[c.SessionId] = new(phone.PhoneId, peer.Id, c.SessionId, c.StreamId, parameters, talk, TimeProvider.System.GetTimestamp()); disposition = "accepted"; detail = "Capture/playback starting; receiving media confirms activity";
                            }
                            break;
                    }
                }
                result = Response(c.RequestId, disposition, code, detail, Status(phone));
            }
            catch (CommandFailure e) { result = Response(c.RequestId, "failed", e.Code, e.Message, Status(phone)); }
            catch (Exception e) { Log($"command={c.Name} request={c.RequestId} error={e.GetType().Name}"); result = Response(c.RequestId, "failed", "OPERATION_FAILED", "Operation failed safely", Status(phone)); }
            ledger.Finish(c.RequestId, result); return result;
        }
        finally { gate.Release(); }
    }
    public static byte[] Response(string id, string disposition, string code, string detail, object? state = null) => Wire.Encode(new { protocol = "LPC1", kind = "response", request_id = id, disposition, code, detail, state });
    private static void Require(Enrollment phone, Grants grant) { if (!phone.Permissions.HasFlag(grant)) throw new CommandFailure("PERMISSION_DENIED", "Enable this phone's permission at the PC"); }
    private async Task StopAsync(StreamOwner owner, CancellationToken ct)
    {
        (owner.Talk ? talks : streams).Remove(owner.Session);
        endedStreams[(owner.Phone,owner.Id)]=TimeProvider.System.GetTimestamp();
        if (links.TryGetValue(owner.Session, out var link) && link.Available)
            try { await link.CallAsync(owner.Talk ? "talk_stop" : "stream_stop", new { stream_id = owner.Id }, ct); }
            catch (Exception e) { Log($"stop session={owner.Session} error={e.GetType().Name}"); }
    }
    private void EndSessionStreams(int session)
    {
        foreach(var map in new[]{streams,talks}) if(map.Remove(session,out var owner)) endedStreams[(owner.Phone,owner.Id)]=TimeProvider.System.GetTimestamp();
    }
    private async Task StopConnectionAsync(string id, CancellationToken ct) { foreach (var s in streams.Values.Concat(talks.Values).Where(s => s.Connection == id).ToArray()) await StopAsync(s, ct); }
    private async Task StopPhoneAsync(string id, CancellationToken ct) { foreach (var s in streams.Values.Concat(talks.Values).Where(s => s.Phone == id).ToArray()) await StopAsync(s, ct); }
    private async Task RevokeAsync(string phone, string sid, CancellationToken ct, PeerConnection? except = null)
    {
        if (registry.Phones.RemoveAll(p => p.PhoneId == phone && p.WindowsSid == sid) == 0) throw new CommandFailure("NOT_PAIRED", "Phone is not enrolled for this user");
        Save(); await StopPhoneAsync(phone, ct); foreach (var c in connections.Values.Where(c => c.PhoneId == phone && c != except)) c.Close();
        if (relayJobs.Remove(phone, out var relayJob)) relayJob.Life.Cancel();
        foreach (var route in routes.Where(r => r.PhoneId == phone).ToArray()) { pendingRelayRevocations.Add(route); routes.Remove(route); }
        store.Save("relay-routes", routes); store.Save("relay-revocations", pendingRelayRevocations);
        ledgers.Remove(phone); foreach (var l in links.Values.Where(l => l.Sid == sid && l.Available)) UpdateObserver(l);
    }
    public async Task ObserveAsync(int session, string reason, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var sid = CoordinatorPipe.SessionSid(session) ?? (states.TryGetValue(session, out var previous) ? previous.WindowsSid : null); if (sid is null) return;
            var now = TimeProvider.System.GetTimestamp(); if (eventDedup.TryGetValue((session, reason), out var last) && TimeProvider.System.GetElapsedTime(last, now) < TimeSpan.FromSeconds(1)) return;
            eventDedup[(session, reason)] = now;
            var status = reason switch { "SessionLock" => "locked", "SessionUnlock" => "unlocked", "SessionLogoff" => "signed_out", _ => "unknown" };
            states[session] = new(session, sid, status, links.TryGetValue(session, out var l) && l.Available);
            var e = new PcEvent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, session, sid, reason, WindowsSessions.User(session));
            events.Enqueue(e); while (events.Count > 128 || events.Count > 0 && events.Peek().ObservedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-5)) events.Dequeue();
            store.Save("events", events.ToArray());
            if (reason == "SessionLock" && pendingLocks.Remove(session, out var completed) && completed.Request.Length == 32)
            {
                var response = Response(completed.Request, "completed", "OK", "Windows confirmed the session lock");
                if (ledgers.TryGetValue(completed.Phone, out var ledger)) ledger.Finish(completed.Request, response);
                foreach (var peer in connections.Values.Where(p => p.PhoneId == completed.Phone)) peer.Send(response);
            }
            if (reason.StartsWith("Power", StringComparison.Ordinal) && proximity.TryGetValue(session, out var policy)) { policy.Health(false); if (links.TryGetValue(session, out var current)) UpdateObserver(current); }
            if (reason == "SessionLogoff" || reason.StartsWith("Power", StringComparison.Ordinal) || reason == "SessionLock" && !OwnerSettings(sid).AllowMediaWhileLocked)
                foreach (var o in streams.Values.Concat(talks.Values).Where(o => o.Session == session).ToArray()) await StopAsync(o, ct);
            foreach (var p in connections.Values.Where(c => registry.Phones.Any(f => f.PhoneId == c.PhoneId && f.WindowsSid == sid))) p.Send(Wire.Encode(new { protocol = "LPC1", kind = "event", pc_id = PcId, @event = e }));
        }
        finally { gate.Release(); }
    }
    private async Task MediaFromCompanionAsync(SessionLink link, MediaFrame frame)
    {
        await gate.WaitAsync(life.Token);
        try
        {
            if (!streams.TryGetValue(link.SessionId, out var owner) || owner.Id != Convert.ToHexString(frame.StreamId).ToLowerInvariant() || frame.Type == 3) return;
            if (connections.TryGetValue(owner.Connection, out var peer)) peer.SendMedia(Frames.EncodeMedia(frame));
        }
        finally { gate.Release(); }
    }
    public async Task TalkFrameAsync(PeerConnection peer, MediaFrame frame, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var stream = Convert.ToHexString(frame.StreamId).ToLowerInvariant();
            if (frame.Type != 3 || frame.Payload.Length is < 1 or > 1275) throw new CommandFailure("UNAUTHORIZED_MEDIA", "Invalid talk packet");
            var owner = talks.Values.FirstOrDefault(o => o.Connection == peer.Id && o.Id == stream);
            // Control Stop can arrive before speech already queued on the media channel.
            // Drop only this authenticated phone's known ended stream; never play it.
            if (owner is null && endedStreams.ContainsKey((peer.PhoneId, stream))) return;
            if (owner is null || !links.TryGetValue(owner.Session, out var link)) throw new CommandFailure("UNAUTHORIZED_MEDIA", "No authorized talk lease");
            talks[owner.Session] = owner with { Renewed = TimeProvider.System.GetTimestamp() }; link.Play(frame);
        }
        finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        life.Cancel(); Pairing.Cancel(); foreach (var c in connections.Values) c.Close(); foreach (var r in relayJobs.Values) r.Life.Cancel();
        foreach (var r in relayJobs.Values) try { await r.Job; } catch (OperationCanceledException) { }
        foreach (var l in links.Values.ToArray()) await l.DisposeAsync(); Identity.Dispose(); life.Dispose();
    }
    private void MaintainRelay(CancellationToken ct)
    {
        if (RelayEndpoint is null) return;
        foreach (var route in routes.Where(r => registry.Phones.Any(p => p.PhoneId == r.PhoneId)))
            if (!relayJobs.ContainsKey(route.PhoneId))
            {
                var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); var endpoint = RelayEndpoint;
                var job = Task.WhenAll(new[] { "control", "media" }.Select(channel => RelayConnector.RunAsync(PcId, route, channel, (socket, prefix, token) => endpoint(channel, route.PhoneId, socket, prefix, token), Log, linked.Token)));
                relayJobs[route.PhoneId] = (linked, job);
            }
        if (pendingRelayRevocations.Count > 0 && !relayJobs.ContainsKey("revocations"))
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); var pending = pendingRelayRevocations.ToArray();
            var job = Task.Run(async () =>
            {
                foreach (var route in pending)
                {
                    try { await RelayConnector.RevokeAsync(PcId, route, linked.Token); await gate.WaitAsync(linked.Token); try { pendingRelayRevocations.Remove(route); store.Save("relay-revocations", pendingRelayRevocations); } finally { gate.Release(); } }
                    catch (Exception e) { Log($"relay revocation pending error={e.GetType().Name}"); }
                }
                await Task.Delay(TimeSpan.FromMinutes(1), linked.Token); await gate.WaitAsync(linked.Token); try { relayJobs.Remove("revocations"); } finally { gate.Release(); }
            }, linked.Token); relayJobs["revocations"] = (linked, job);
        }
    }
}
