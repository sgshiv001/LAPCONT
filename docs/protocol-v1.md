# LPC1 protocol — implemented baseline 0.1.0

The baseline product implements these envelopes, command schemas and framing in C# and Kotlin.
The retained Phase 0 echo is isolated from the product and executes no business command.

## Channels and transport bounds

Independent `/control` and `/media` WebSockets carry **only inner TLS bytes** as binary chunks.
One WSS message is at most 65,536 bytes; fragments must remain within that total. Each direction
has bounded queues. Kotlin carrier receive queue: 16 chunks, approximately 1 MiB maximum;
queued writes fail above 64 KiB rather than growing indefinitely. The duplex engine has one
reader, a separate write mutex and short provider synchronization. OkHttp checks complete
message size at callback time; hostile pre-callback allocation is an outstanding client-boundary
test, not a claimed hard memory guarantee. The relay must enforce the same wire bound at read.

Handshake size/time budget: 256 KiB cumulative carrier traffic and 10 seconds per connection
are enforced by the product. Maximum authenticated channel life is ten minutes/1 GiB;
Android initiates a fresh connection at nine minutes. Failed/expired/version-mismatched handshakes close
the carrier, release resources, and execute no action.

After inner TLS, application messages have a **four-byte unsigned big-endian length prefix**,
then exactly that many bytes. Control length: 1–4096. Media length: 40–1,048,616 (40-byte header
plus payload up to 1 MiB). Validate length before allocation. No application fragmentation is
defined in v1; a larger media access unit is rejected and capture quality must be reduced.
TLS/WSS fragmentation is transparent and bounded by their adapters.

An initial protected context carries `protocol=LPC1`, `kind=context`, channel, enrolled PC/phone
IDs and fresh 128-bit connection-session ID. Wrong context/version/peer is fatal. The control
context includes PC UTC time and a random 32-byte media attachment token. A media_attach envelope
contains exactly protocol, kind, pc_id, phone_id, connection_session_id and media_attachment;
the token is single-use, expires after ten seconds, and binds the separately authenticated media
identity to the current control session. Independent handshakes alone do not provide that binding.

## Control example

Illustrative values are not credentials or operational requests:

```json
{
  "protocol": "LPC1",
  "kind": "command",
  "request_id": "638c7655579e4b5f9343fa7a4cb27b96",
  "pc_id": "enrolled-pc-id",
  "phone_id": "enrolled-phone-id",
  "connection_session_id": "4eca7cd6298444f6ae8eb8d3e9531c8b",
  "sequence": 42,
  "issued_at_utc": "2026-10-02T10:00:00Z",
  "expires_at_utc": "2026-10-02T10:00:10Z",
  "command": "lock",
  "params": { "windows_session_id": 2 }
}
```

TLS authenticates every field. Reject invalid UTF-8, duplicate object keys, root values other than
objects, nesting over 12, unknown top-level/parameter fields, wrong types, and out-of-range values.
`Frames.ParseControl` validates UTF-8/duplicates/depth/size. Command.Parse validates the exact
envelope and parameters; the coordinator rechecks enrollment/grants/SID/session before execution.

PC/phone IDs are generated 32-hex enrollment identifiers, with request/session IDs
32 hex characters representing 16 bytes, no .NET Guid mixed-endian conversion. Sequence is an
integer 1 through 2^63−1, strictly increasing per connection/direction. Reject old connection
sessions. Keep bounded per-peer request-id outcomes across reconnect for two minutes so retries
cannot repeat side effects. Use the same request ID for retry only to query/receive its prior
outcome; new deliberate actions need a new ID. Cache overflow rejects new actions or expires old
entries; it never repeats an old action because an outcome was evicted.

PC time is authoritative. Clock-skew tolerance is five seconds for `issued_at_utc`; expiration
must be after issuance, no more than ten seconds later, and not past PC time. Skew/expiry is
reported before execution. Media lease renewal uses fresh `stream_start` for the same owned
stream ID and validated unchanged parameters while connected, never an old replayed request.
Local delays, leases, and filtering use monotonic time. Reconnection fetches fresh `status`,
resets transport sequence/context, and does not automatically restart media or resend actions.

## Ten baseline business commands

| Command | Parameters / behavior |
|---|---|
| `pair` | Separate `/pair` bootstrap, exact fields: protocol, kind, command, pairing_token, phone_id, phone_name; atomic 60-second token plus PC confirmation |
| `status` | Empty params `{}`; fresh scoped state/capabilities and five-minute event catch-up, up to three events/two sessions per bounded response |
| `lock` | `windows_session_id`; scope/grant/companion checks; accepted then completed on matching Windows event |
| `unlock_request` | `windows_session_id`; `SIGN_IN_REQUIRED`; keep actual lock state |
| `stream_start` | Exactly windows_session_id, stream_id, video, audio, width, height, fps, bitrate, recovery; independently granted tracks; 30-second lease |
| `stream_stop` | owned stream ID; idempotent release |
| `talk_start` | authorized session/stream ID; short lease and inactivity timeout; push-to-talk |
| `talk_stop` | owned stream ID; idempotent release |
| `calibrate` | session ID, operation `start/cancel/query`; valid timestamped BLE buckets required |
| `unpair` | Empty params `{}`; revoke the authenticated phone's own enrollment and active permissions/leases |

For `stream_start`, supported negotiated presets are initially 640×480/1 Mbps or
1280×720/2 Mbps/30 fps. No 1080p preset is exposed in this build. At least one track must be true;
both presets require exactly 30 fps even for audio-only commands. Lease renewal may request
`recovery=true` to ask for fresh SPS/PPS and an IDR without adding an eleventh command.
Stop must use independent control queues so video does not block it.
An active renewal requires the same phone/control context, session, stream ID and unchanged
tracks/preset; recovery alone may change. It requests a fresh IDR with SPS/PPS without reopening
the camera. Ended stream IDs are tombstoned for ten minutes; reconnect/Stop/fault requires a fresh
ID and request. Unknown/missing fields fail. windows_session_id must be a positive integer;
stream_id is exactly 32 hex characters. talk_start requires exactly windows_session_id/stream_id;
stop commands require exactly stream_id. Calibration operation is exactly start/cancel/query.
The pairing result includes protected scope/grants and separate proximity material after consent.

Responses have `kind=response`, matching request ID, `accepted/completed/failed`, safe stable
error code/detail, and verified state when available. Events have `kind=event`, a unique event ID,
UTC observation time, PC ID, affected Windows session ID, verified actor or explicit unknown.
Connection, lock, media and proximity states are independent; offline/stale must be labelled.
Handshakes, contexts, responses, events and keepalives are transport/envelope types, not business
commands. No `unlock`, shutdown, reboot, logoff or arbitrary execution command exists.

## Media header: exactly 40 bytes

All multi-byte values are big-endian. Payload length excludes the header.

| Offset | Width | Field |
|---:|---:|---|
| 0 | 1 | framing version, `1` |
| 1 | 1 | type, `1..5` |
| 2 | 2 | flags: bit 0 keyframe; bit 1 discontinuity; other bits zero |
| 4 | 16 | stream ID, raw bytes as represented by its 32-hex ID |
| 20 | 8 | sequence, 0..2^63−1, increasing per stream/direction |
| 28 | 8 | nonnegative presentation timestamp, microseconds from sender monotonic stream origin |
| 36 | 4 | payload length, 0..1,048,576; exact frame length required |
| 40 | variable | authenticated payload |

| Type | Direction | Payload |
|---|---|---|
| 01 | PC → phone | complete H.264 access unit, Annex B |
| 02 | PC → phone | one Opus packet, 960 mono samples/20 ms at 48 kHz |
| 03 | phone → PC | one talk-back Opus packet |
| 04 | PC → phone | codec config: SPS/PPS Annex B; precedes dependent access units |
| 05 | PC → phone | bounded UTF-8 metadata: negotiated video/audio format and monotonic origin |

Initially max Opus payload 1275 bytes. Receiver rejects sequence/stream/direction mismatch,
unknown types/flags, length mismatch, negative PTS and unauthorized track data. Header flags
cannot replace H.264 parsing/decoder dependency checks. IDR on start/recovery and about every
two seconds; no B-frame/reordering where supported. Format changes send new metadata/config
and reset decoder/jitter state. Drop dependent stale video only with recovery/IDR; bounded
queues cannot silently treat arbitrary NAL fragments as complete frames.

PC media queues are bounded to 32 frames and 4 MiB per queue, including in-flight writes;
the Android receiver is bounded to 64 frames and 4 MiB. Overflow ends the affected connection
or stream. Decoder input-buffer retries drain output and preserve the current access unit,
with a 500 ms stall deadline. These bounds are safety limits, not measured latency promises.
The PC capture worker allows a cancellable 250 ms enqueue wait before treating a full IPC queue
as persistent backpressure. Timed-out pending writes are canceled, preserving Stop semantics.

The receiver maps sender monotonic PTS to its playback clock with explicit discontinuity reset,
uses audio as playback master and bounded jitter buffering. Capture-to-render latency requires
sender/receiver instrumentation or a visible time reference; RTT alone is not media latency.
The product receiver performs timestamp mapping; full capture-to-render latency remains unmeasured.
The published command timings are supplementary emulator status RTT, not media latency.

## Shared framing vector

Type 1, keyframe flag, stream bytes 00..0F, sequence 42, PTS 33333 μs, six-byte **abbreviated test
payload** 000000016588. This payload tests framing, not decodable video.

```text
01010001000102030405060708090A0B0C0D0E0F000000000000002A000000000000823500000006000000016588
```

C# `FramingTests` and Kotlin `FramesTest` compare this exact vector and malformed lengths.
TLS interoperability uses runtime identities, not fixed private-key vectors. Identity rejection,
record corruption and record repetition are exercised with actual provider handshakes.
