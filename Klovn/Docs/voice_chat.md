# In-game voice chat

Proximity voice chat. The game client has no microphone access, so talkers use a web page that the game server
serves itself. Listening needs no setup at all. This document covers how audio gets from a player's microphone to
nearby players, why each piece is built the way it is, how the link is protected, and how to run it on a server.

## Background

The content sandbox gives client code no way to capture audio. The engine never opens a capture device, and the CEF
webview explicitly denies media permission requests. The one route out is `IUriOpener`, which opens a URL in the
player's own browser. So a player who wants to talk:

1. gets a personal link from the game: Options → Klovnstation 14 → *Set up microphone...*, the `voicechat` command
   (link window), the `voicelink` command (opens the page in the browser straight away), or by pressing push-to-talk
   with no microphone page connected;
2. opens it in their browser and allows microphone access;
3. holds the in-game push-to-talk key (default `N`, rebindable) to talk.

The browser streams microphone audio to the game server over a websocket. The server checks that the player is allowed
to talk, moderates the audio, and relays it to the players around them, whose clients play it from the talker's
position. A sprite above the talker shows who is speaking.

The browser can't see in-game keypresses, which is why push-to-talk is sent from the game client to the server and the
server does the gating. The page streams whenever its own noise gate is open. The server drops everything that isn't
covered by a held key.

## Architecture

```
browser page ──ws: auth, 16 kHz PCM──▶ status host ─▶ KsVoiceUplinkConnection (thread pool)
                                                        │ validate, rate-limit, KsVoiceProcessor
                                                        ▼
game client ──KsVoicePushToTalkEvent──▶ KsVoiceSystem (main thread) ◀── inbound queue
                                           │ gate: enabled, PTT held, mutes, CanSpeak
                                           │ IMA ADPCM encode
                                           ▼
nearby game clients ◀── KsVoiceFrameMessage (unreliable) ── KsVoicePlaybackSystem ─▶ positional chunks
```

### The page and the uplink

`KsVoiceUplinkManager` registers a handler on the engine's status host, the same HTTP server that answers `/status`
and `/info`. It serves the page from files embedded in `Content.Server` (`_KS14/Voice/Web/`, logical names `KsVoice.*`)
under `/klovn/voice/`, and accepts websockets at `/klovn/voice/ws`.

- **Why the socket loop runs on its own task.** The status host counts every request against
  `status.max_connections` (default 5) until its handler returns. A handler that awaited a websocket for its whole
  lifetime would let five talkers stop the server answering the hub. So the handler accepts the socket, hands
  `KsVoiceUplinkConnection.RunAsync` to `Task.Run`, and returns at once. `ProcessRequestAsync` does nothing with the
  context after the handler returns, so the socket survives.
- **Releasing the connection.** The listener gives websockets a stream that doesn't own its socket. Disposing the
  websocket after the close handshake would therefore leave the TCP connection in `CLOSE_WAIT` for the life of the
  server, and leave the browser waiting for a close that never comes. Once the socket loop ends, the manager aborts
  the listener response, which it reaches through the engine's private `StatusHost.ContextImpl._context` by reflection
  (server content isn't sandboxed). If a future engine renames that field, voice still works: a warning is logged once
  and connections are released only when clients drop them.
- **Why the page needs HTTPS.** Browsers only grant `getUserMedia` in a secure context: HTTPS, or `http://localhost`.
  The status host speaks plain HTTP, so a public server needs a TLS reverse proxy (see *Running it*). The page
  detects an insecure context and says so rather than failing silently.
- **Protocol, page to server.** First a text message `{"type":"auth","token":"..."}` within five seconds. Then binary
  audio: `[u8 version=1][u8 reserved][u16 sequence LE][int16 LE samples]`, carrying one to three whole 20 ms frames of
  16 kHz mono (at most 1924 bytes). Optionally, text keepalives `{"type":"ping"}`. Anything else closes the socket.
- **Protocol, server to page.** Text JSON: `hello` (with the player's username), `state` (whether audio is being
  transmitted and, if not, why, plus seconds remaining for timed blocks), and `closing` (reason) before the server
  hangs up.
- **What the page shows.** The page asks for the microphone as soon as it opens, so the browser's permission prompt
  appears without a click; declining leaves a "Start microphone" button and says how to allow it. `state` only says
  whether the server *would* relay audio (push-to-talk held, nothing blocking), so the page combines it with its own
  microphone state: with the microphone off it never claims to be transmitting, and says so if push-to-talk is held.
  Firefox also holds any audio graph started without a click on the page (allowing the microphone in its prompt
  doesn't count), so there the page asks for one click and carries on; Chromium browsers start without one.
- **Resampling in the worklet.** The page captures at the device's own rate and resamples to 16 kHz inside the
  AudioWorklet, using a box filter that also acts as a crude low-pass. Creating the AudioContext at 16 kHz would be
  simpler, but Firefox refuses to connect a microphone stream to a context whose rate differs from the device's.

### Security model: the link is the credential

The requirement is that nobody can talk as another player unless that player hands over their link.

- **The token.** 256 bits from `RandomNumberGenerator`, base64url-encoded. It is issued only to the session that asks,
  over that session's own game connection, and is held in memory keyed by `NetUserId`. Lookups go through its SHA-256,
  with a constant-time comparison on a hit. Asking again returns the same token. *Reset link* issues a new one, which
  invalidates the old token and disconnects any page using it. Resets are rate limited to one every 5 s per player,
  since each disconnects the page and writes an admin log entry. Disconnecting from the server, or restarting it, also
  invalidates the token.
- **The token lives in the URL fragment** (`https://host/klovn/voice/#<token>`). Browsers never send the fragment in
  any request. So the token can't appear in the status host's request log (which logs `PathAndQuery` at Info), in
  reverse-proxy access logs, or in a `Referer` header. The page moves it into `sessionStorage`, strips it from the
  address bar straight away, and sends it as the first websocket message. The in-game window never displays the link
  itself; it only opens or copies it, so it doesn't show up on stream.
- **Identity is server-side.** Audio is attributed only to the `NetUserId` the token resolved to. The page sends no
  identity at all, and the talker's body is whatever that session is attached to at the time.
- **Other defences:**
  - a second page for the same player replaces the first
  - failed authentications are counted per client address, and addresses over
    `klovn.voice.auth_failures_per_minute` get `429`; behind a local proxy, the proxy-appended `X-Forwarded-For`
    entry is trusted only when the request itself comes from loopback
  - the websocket's `Origin` must match the public URL or the request's own host (`klovn.voice.check_origin`)
  - the page is served with a strict CSP, `frame-ancestors 'none'`, `Referrer-Policy: no-referrer` and
    `Cache-Control: no-store`

### Relay

`KsVoiceSystem` drains the inbound queue on the main thread. It relays a chunk only if all of these hold:

- `klovn.voice.enabled` is on;
- the talker holds push-to-talk (`KsVoicePushToTalkEvent`, released automatically when they leave a body);
- they aren't admin-muted, auto-muted, or on a continuous-talk cooldown;
- their attached entity isn't a ghost and passes `ActionBlockerSystem.CanSpeak`. So crit, death, sleep, mime vows and
  admin freeze-mutes silence voice exactly as they silence speech, through `SpeakAttemptEvent`, with nothing extra to
  maintain. `CanSpeak` runs on every chunk, so a block that starts mid-sentence cuts the voice at once. Some of the
  handlers that refuse speech also show a popup (`MutingSystem`'s "You can't speak right now!"), so a refusal is
  remembered for that body until push-to-talk is next pressed: one popup per attempt to talk, not one per chunk.

A relayed chunk is IMA ADPCM-encoded once and sent as a `KsVoiceFrameMessage` to every other in-game player whose
entity:
- is on the same map and within `klovn.voice.range`;
- is either a ghost, or neither incapacitated nor asleep;
- can see the speaker. This is the same visibility-layer rule PVS uses: the listener's eye mask must cover every layer
  of the speaker's `VisibilityMask`. Voice therefore never reaches anyone the speaker's entity is never sent to.

Ghosts listen but never talk, and living players never hear them. Ghosts fail both the ghost check and `CanSpeak`
(observers have no `SpeechComponent`), and the ghost visibility layer is outside living eyes anyway. The
message is `Unreliable`, because audio that arrives late is useless. A per-talker sequence number lets clients
reorder, and each packet carries its own ADPCM predictor state, so one lost packet never corrupts the next.

**Why ADPCM.** Client content can load only `Content.*` assemblies, and their IL is verified. No Opus decoder is
reachable, and the engine's Vorbis loader is internal. IMA ADPCM is about 150 lines of sandbox-safe C#
(`KsVoiceAdpcm`) and gives 64 kbps for 16 kHz speech. That's only while someone is actually talking, and 60 ms packets
stay well under the default 700-byte MTU.

The talking indicator is appearance data (`KsVoiceVisuals.Talking`) on `KsVoiceIndicatorComponent`. It is added the
first time an entity talks and cleared 300 ms after the last relayed chunk. `KsVoiceIndicatorVisualizerSystem` draws
it, following the typing indicator's approach. The sprite sits on the opposite side of the head from the typing bubble,
so the two don't overlap. Unlike the typing bubble it is lit normally (no `unshaded` shader), so it doesn't glow in the
dark and give away someone talking in an unlit room.

### Playback

The engine exposes no streaming audio source to content, because `IBufferedAudioSource` is internal. What content can
use is `IAudioManager.LoadAudioRaw` and `CreateAudioSource`, which play one fixed buffer each. `KsVoicePlaybackSystem`
therefore:

1. reorders each talker's packets into a jitter buffer. Playback starts once `klovn.voice.jitter_buffer_ms` of audio is
   buffered, and a packet is given up as lost once three later ones have arrived;
2. plays the audio as a chain of 120 ms chunks. Each chunk also carries the next 20 ms of audio, faded out, and the
   following chunk starts with those same samples, faded in, 20 ms before the current one ends (see below);
3. positions every source by hand each frame, as the engine's MIDI renderer does for its own streaming sources: map
   position, occlusion from `AudioSystem.GetOcclusion` (refreshed every 100 ms per talker, not every frame, since it is
   a physics raycast), and silence when the talker is on another map or out of range.

**No audio entities.** Chunks are raw OpenAL sources, not entities: nothing about playback is spawned, networked or
subject to PVS. The only networked inputs are the voice packets themselves and the talker's entity, which supplies
the position. The talking indicator is the one piece of entity state voice touches: two appearance changes per
utterance.

**Frame-rate independence.** Playback only runs once per frame, so it can never start a chunk at exactly the right
moment. `KsVoiceChunkTiming` handles that in two steps:

- it starts the next chunk on the last frame *before* the start point, judged against a peak-held estimate of the
  frame time;
- it then corrects the error exactly. A chunk started early gets that much leading silence, and one started late
  (after a hitch longer than the estimate) is skipped ahead by the lateness.

The remaining time comes from the playing source's own `PlaybackPosition`, which is in the mixer's time, so the new
chunk's first real sample lands on the intended mixer sample. Low or uneven frame rates therefore don't shift the
crossfade: no gaps, no doubled audio, no comb filtering.

**Where the state lives.** Each talker's jitter buffer and playing chunks, and whether the local player muted them,
live on the talker's entity in the client-only `KsVoicePlaybackComponent`. A finished utterance's state is dropped
after two seconds of silence; the component goes with it unless the talker is muted, since the mute should outlast
the utterance. It dies with the entity, which covers going out of view (the entity is only detached) but not a round
restart.

**Talkers the client can't place yet.** Voice packets and entity state travel separately, so a talker's first packets
can arrive before their entity does, for example as they walk into view. With no entity to hold it, their audio waits
in a small holding list (the newest jitter buffer's worth) and moves onto the entity as soon as it arrives, instead of
being dropped, which used to cut off the start of what they said.

## Moderation

- **Limiter.** `KsVoiceProcessor` runs on every uplink chunk before anything else sees it. A DC blocker comes first,
  then a peak limiter whose envelope follows peaks instantly and decays over 150 ms. No relayed sample can exceed
  `klovn.voice.limiter_ceiling_db`.
- **Auto-mute.** A 20 ms frame counts as abusive if its RMS is above `klovn.voice.abuse_rms_db`, or if more than
  `klovn.voice.abuse_clip_ratio` of its samples are clipped. `klovn.voice.abuse_seconds` of abusive frames within the
  last 10 s of *transmitted* audio mutes the talker for `klovn.voice.auto_mute_seconds`. That also posts an admin
  alert, writes a high-impact admin log entry, and tells the player with a popup and on the page. Silence neither
  accumulates nor forgives anything.

  "Transmitted" is literal. The server tells each page connection whether its audio is being relayed, and only then
  do frames count, so a page left open in a loud room with push-to-talk up can never earn a mute. The server also
  re-checks when the mute would apply, because that flag trails by a chunk. A talker who is already auto-muted can't
  be auto-muted again, so a noisy open mic produces one alert, not one every few seconds.
- **Duration and rate.**
  - Talking continuously for `klovn.voice.max_continuous_seconds` triggers a `klovn.voice.cooldown_seconds` cooldown.
  - A page sending audio faster than `klovn.voice.uplink_rate_factor` × real time, beyond a half-second burst, is
    disconnected.
- **Admins.** All require `Moderator`, and mutes are keyed by user, so they follow the player across bodies and
  reconnects.

  | Command or verb | Effect |
  | --- | --- |
  | `vcmute <player> [minutes, 0 = rest of round] [reason...]` | Mutes the player. Timed mutes run on real time and survive round restarts; round mutes lift at round end. |
  | `vcunmute <player>` | Lifts a mute. |
  | `vcmutes` | Lists active mutes. |
  | *Voice mute (round)* / *Voice unmute* | Admin-menu verbs on a player. |

- **Players.** A client-side *Mute voice* verb on anyone who has talked silences them locally.
- **Who can run what.** The moderation commands require `Moderator`, and a test checks that a player without admin
  rights is refused. The player commands `voicechat` (opens the link window) and `voicelink` (opens the voice page in
  the browser directly) are `[AnyCommand]`. Client commands without it are admin-only, so the test checks them as a
  de-adminned player.
- **Admin log.** Everything is logged under `LogType.KsVoice`: microphone pages connecting, link resets, talk bursts
  (optional, `klovn.voice.admin_log_bursts`), and mutes and auto-mutes.

## CVars

Voice is **off by default**. Every entry point checks `klovn.voice.enabled`. With it off:

- the page and websocket answer 404
- no links are issued
- open pages are disconnected
- nothing is relayed
- clients hide the options section and window, and ignore the push-to-talk key. The options tab follows this switch,
  and the voice cvars, live while it's open, rather than only when the options menu is opened

| CVar | Default | Side | Purpose |
| --- | --- | --- | --- |
| `klovn.voice.enabled` | `false` | server, replicated | Master switch. |
| `klovn.voice.uplink_enabled` | `true` | server, replicated | Serve the page and accept microphone pages. Off disconnects every page, so no one can talk; players reopen their link once it's back on. |
| `klovn.voice.range` | `10` | server, replicated | Hearing range in world units (the same as local speech). |
| `klovn.voice.public_url` | `""` | server | Public HTTPS base URL of the status host. If empty, derived from `hub.server_url` (`ss14s://h` → `https://h`), then `transfer.http_endpoint`. |
| `klovn.voice.check_origin` | `true` | server | Require websocket `Origin` to match. |
| `klovn.voice.limiter_ceiling_db` | `-6` | server | Output peak ceiling, in dBFS. |
| `klovn.voice.abuse_rms_db` | `-9` | server | Frame RMS that counts as abusive, in dBFS. |
| `klovn.voice.abuse_clip_ratio` | `0.05` | server | Share of clipped samples that counts as abusive. |
| `klovn.voice.abuse_seconds` | `3` | server | Abusive seconds (within 10 s of audio) before auto-mute. 0 disables detection. |
| `klovn.voice.auto_mute_seconds` | `120` | server | Length of an auto-mute. 0 disables auto-mute. |
| `klovn.voice.max_continuous_seconds` | `60` | server | Longest talk without a break. 0 disables. |
| `klovn.voice.cooldown_seconds` | `3` | server | Cooldown after the above. |
| `klovn.voice.uplink_rate_factor` | `1.25` | server | Allowed uplink speed relative to real time. |
| `klovn.voice.auth_failures_per_minute` | `10` | server | Failed authentications per address before `429`. |
| `klovn.voice.admin_log_bursts` | `true` | server | Log every talk burst. |
| `klovn.voice.hear_enabled` | `true` | client | Play other players' voices. |
| `klovn.voice.volume` | `1` | client | Voice volume. |
| `klovn.voice.jitter_buffer_ms` | `120` | client | Buffering before playback starts. |

## Running it

1. Put the status host behind a TLS reverse proxy that forwards websocket upgrades. Most public servers already do
   this for `ss14s://`.

   nginx:

   ```nginx
   location /klovn/voice/ {
       proxy_pass http://127.0.0.1:1212;
       proxy_http_version 1.1;
       proxy_set_header Upgrade $http_upgrade;
       proxy_set_header Connection "upgrade";
       proxy_set_header Host $host;
       proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
       proxy_read_timeout 1h;
   }
   ```

   Caddy (which proxies websockets automatically):

   ```
   ks14.example.com {
       reverse_proxy 127.0.0.1:1212
   }
   ```

2. Enable it, in the server config or at runtime:

   ```toml
   [klovn.voice]
   enabled = true
   public_url = "https://ks14.example.com"
   ```

For local testing, `http://localhost:1212` is already a secure context, so no proxy is needed. Leave `public_url`
empty and the transfer endpoint default (`http://localhost:1212/`) is used.

## Key files

| Path | Role |
| --- | --- |
| `Content.Shared/_KS14/CCVar/KsCCVars.Voice.cs` | All voice cvars. |
| `Content.Shared/_KS14/Voice/KsVoiceAdpcm.cs` | IMA ADPCM codec. |
| `Content.Shared/_KS14/Voice/KsVoiceMessages.cs` | Relay net message; PTT, link and status network events. |
| `Content.Shared/_KS14/Voice/KsVoiceIndicatorComponent.cs` | Talking indicator component and appearance keys. |
| `Content.Server/_KS14/Voice/KsVoiceLinkManager.cs` | Token issue, lookup, revocation; public URL. |
| `Content.Server/_KS14/Voice/KsVoiceUplinkManager.cs` | Status-host routes, page files, connection registry, auth throttling. |
| `Content.Server/_KS14/Voice/KsVoiceUplinkConnection.cs` | One page's websocket: auth, parsing, rate limit, processor. |
| `Content.Server/_KS14/Voice/KsVoiceProcessor.cs` | Limiter and abuse detection. |
| `Content.Server/_KS14/Voice/KsVoiceSystem.cs` | Gating, relay, indicator, PTT and link events. |
| `Content.Server/_KS14/Voice/KsVoiceSystem.Moderation.cs` | Admin mutes, auto-mute, admin verbs. |
| `Content.Server/_KS14/Voice/Commands/KsVoiceMuteCommands.cs` | `vcmute`, `vcunmute`, `vcmutes`. |
| `Content.Server/_KS14/Voice/Web/` | The microphone page. |
| `Content.Client/_KS14/Voice/KsVoicePlaybackSystem.cs` | Jitter buffer, crossfaded chunk playback, positioning, local mutes. |
| `Content.Client/_KS14/Voice/KsVoicePlaybackComponent.cs` | Per-talker playback state and local mute, on the talker's entity. |
| `Content.Client/_KS14/Voice/KsVoiceChunkTiming.cs` | When the next chunk starts, and how it is padded or skipped to line up. |
| `Content.Client/_KS14/Voice/KsVoiceUIController.cs` | Push-to-talk key and link window. |
| `Content.Client/_KS14/Voice/KsVoiceIndicatorVisualizerSystem.cs` | Talking sprite. |
| `Resources/Textures/_KS14/Effects/voice_indicator.rsi` | Talking sprite art. |

## Tests

All under `Content.IntegrationTests/Tests/_KS14/Voice/`.

- `KsVoiceCodecTests`:
  - ADPCM round trip, and a packet decoding on its own after its predecessor is lost
  - rejection of malformed packets
  - limiter ceiling; abuse triggering exactly once at the threshold, and never for loud normal speech
  - public URL resolution
- `KsVoiceUplinkTests`, using real websocket framing over loopback TCP:
  - the token appears only in the fragment
  - wrong tokens and audio before auth are rejected; a valid token attributes audio to its owner
  - reset and revoked tokens stop working
  - malformed, oversized and faster-than-real-time audio each close the socket
  - a second page replaces the first
  - disabled voice refuses even valid links
- `KsVoiceRelayTests`, where a dummy session talks and the pooled client listens over the real net channel:
  - relay only while push-to-talk is held
  - range
  - the master switch
  - admin mute and unmute
  - freeze-mute through `CanSpeak`
  - auto-mute
  - the indicator showing and clearing on the client
  - audio for a talker whose entity the client doesn't know yet being kept, not dropped, and moving onto the entity
    once it arrives
  - a local mute living on the talker's entity and outlasting their audio; unmuting a silent talker removing it
  - a muted talker getting one "can't speak" popup per push-to-talk press, not one per chunk
- `KsVoiceOptionsTabTests`: the options tab showing the voice section when the server turns voice on and following a
  volume change made elsewhere while open, and letting go of the configuration manager once closed
- `KsVoiceLinkWindowTests`: resizing the link window never cuts anything off. Its minimum size follows its contents
  (the wrapping text gets taller as the window narrows), so a fixed minimum couldn't guarantee that
- `KsVoiceChunkTimingTests`: early starts padded and late starts skipped to the exact sample, skips never
  exceeding what was already heard, and the start decision at different frame rates

Crossfade quality and the sprite's look need a real client with speakers to check.

The page itself has no automated tests in the suite; it was driven by hand in headless Chromium and in Firefox 156
(over WebDriver BiDi, with PulseAudio providing a null sink), against a mock of the voice endpoints. In both, the
microphone streams at real time (Firefox from a 44.1 kHz device, so through the non-integer resampling path), and the
status line reads correctly with the microphone on, off, refused, or waiting on the browser.
