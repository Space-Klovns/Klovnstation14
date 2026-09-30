# Text-to-speech

Speech is voiced by an external TTS endpoint. The server asks it for each line and relays the clip to everyone
who can see the speaker. Players pick their character's voice in the character editor, and can hear it first
from a preview.

## Voices

A voice is a `ttsVoice` prototype (`Resources/Prototypes/_KS14/TTS/voices.yml`):

```yaml
- type: ttsVoice
  id: en_GB-alan-medium
  voice: en_GB-alan-medium          # the endpoint's name for it
  name: tts-voice-en-gb-alan-medium # what the character editor shows
  selectable: false                 # optional, default true
```

- **`name`** is required. The names live in `Resources/Locale/en-US/_KS14/tts/voices.ftl`.
- **`selectable: false`** keeps a voice back for particular entities, such as the elite combatant voice. Such a voice:
  - isn't offered in the character editor;
  - is dropped from a profile that holds one, which then falls back to random (`HumanoidCharacterProfile.EnsureValid`);
  - is never handed out at random;
  - still plays for any entity whose `TtsVoice` component names it.

## Choosing a voice

- **The profile field.** `HumanoidCharacterProfile.TtsVoice` (`HumanoidCharacterProfile.Klovn.Tts.cs`) is stored in
  the `profile.tts_voice` column (migration `KsTtsVoice`). Null means random.
- **On spawn.** `TtsSystem` copies the voice onto the body's `TtsVoiceComponent` (`PlayerSpawnCompleteEvent`).
- **Random voices.** A body with no voice gets a random selectable one the first time it speaks. The same happens to
  a body whose voice a prototype reload has since removed.

## The endpoint

The server POSTs JSON and expects audio back:

```json
{ "text": "Hello, crew.", "voice": "en_GB-alan-medium" }
```

With `klovn.tts.codec opus`, the body also carries `"format": "opus"`. That's the only change to the request; the other
two modes, including the default, send exactly what was always sent. The server works out what came back from the bytes themselves
(`KsTtsOpus.Identify`), not from the setting. An endpoint that ignores `format` still works, and anything that is
neither Ogg Vorbis nor Ogg Opus is dropped with a warning, not sent to clients.

| `klovn.tts.codec` | Asks for | Sends clients |
| --- | --- | --- |
| `vorbis` | the endpoint's default | what came back, untouched: the behaviour before this work |
| `opus` | `"format": "opus"` | what came back, untouched |
| `transcode` (default) | the endpoint's default | Ogg Opus, re-encoded by the server from Ogg Vorbis or 16-bit WAV |

Other cvars:

- `klovn.tts.opus_bitrate` (default 32000) and `klovn.tts.opus_complexity` (default 10) only apply to `transcode`.
- `klovn.tts.enabled` and `klovn.tts.endpoint` are as before.

### Why Opus, and why the client decodes it

The engine can only play Ogg Vorbis (`IAudioManager.LoadAudioOggVorbis`), and loading one has to happen on the
game thread. Opus is the better codec for speech at these bitrates, and the voice chat work already vendored a
pure-C# Opus decoder, Concentus (`Content.Klovn.Concentus`, a project of its own). So Opus clips take a different
path:

1. **Demultiplex and decode.** `KsTtsOpus` splits the Ogg pages (RFC 7845) and decodes the packets. This runs on
   the thread pool, since it is ordinary content code.
2. **Hand over the samples.** The engine then gets raw samples through `LoadAudioRaw`, which is only a buffer
   upload.

Stereo is mixed down to mono, because OpenAL only positions mono sources.

`transcode` exists for endpoints that can't serve Opus. It saves bandwidth, and moves the client's decoding off the
game thread. It can't restore what a lossy source already threw away: re-encoding Vorbis stacks two lossy codecs.
For better sound, serve Opus from the endpoint (`opus`), or serve WAV and let the server encode it (`transcode`).

## Previews

The character editor's **Preview** button plays the chosen voice saying `tts-preview-text`. Previews are baked by
the server, not requested on demand. `KsTtsPreviewManager` is a manager rather than a system, because the editor
lives in the lobby and the net message has to be registered before anyone connects.

### When they are baked

- **Once.** The server bakes one clip per selectable voice, the first time TTS is on. It asks one voice at a time,
  so as not to bury the endpoint.
- **On failure.** It stops at the first failure and tries again after a minute. That's one warning per attempt, not
  one per voice.
- **On a prototype reload.** Previews for voices that were removed, made unselectable, or pointed at a different
  `voice` are dropped, and clients get the full set again. Missing voices are then baked.

### Delivery and decoding

- **Delivery.** A connecting client gets every preview baked so far, then each new one as it is baked. The previews
  travel on their own reliable sequence channel, so a few hundred kilobytes of them can't hold up the messages that
  get a player into the game.
- **Opus previews** are decoded on the thread pool as they arrive.
- **Vorbis previews** can only be decoded on the game thread. So they wait until they are first played: one short
  clip when the player asks for it, not every voice at once while connecting.

### The Preview button

Its state and tooltip come from `KsTtsPreviewManager.GetAvailability`:

| State | Button | Why |
| --- | --- | --- |
| `TtsDisabled` | off | `klovn.tts.enabled` is off, so no voice is heard in game |
| `NoVoice` | off | Random is picked |
| `Baking` | off | the server hasn't finished yet |
| `Failed` | off | the endpoint failed; the server retries |
| `Missing` | off | the server finished without one for this voice |
| `Decoding` | off | arrived, still being decoded |
| `Broken` | off | arrived, but couldn't be decoded |
| `Available` | on | |

The button updates live while the editor is open.

## Rate limit and length

- **Cooldown.** A speaker's lines go unvoiced for 0.5 s plus 21 ms per character after each voiced line
  (`TtsVoiceComponent.CooldownEnd`).
- **Length.** Lines are cut to their first 50 characters.

Both of these were broken before this work:

- the cooldown's cleanup was inverted, so it expired after a single tick;
- the cut kept all but the last 50 characters, so a 51-character line became one character.

## Tests

All in `Content.IntegrationTests/Tests/_KS14/TTS/`:

- **`KsTtsCodecTests`**
  - decoding a clip that libopus itself encoded (`KsTtsFixtures`), sample for sample;
  - round trips through our own encoder at 16, 24 and 48 kHz;
  - Ogg page CRCs and flags;
  - 500 corrupted files, none of which may throw;
  - the transcoder, from Vorbis and from WAV;
  - the resampler.
- **`KsTtsPreviewTests`**: previews end to end, against a real HTTP endpoint on loopback (`KsFakeTtsEndpoint`):
  - nothing is baked while TTS is off;
  - each preview is baked once, and reconnecting clients receive them;
  - Opus and transcoded previews;
  - retries after failure;
  - prototype reloads;
  - the editor's button and voice list.
- **`KsTtsProfileTests`**
  - profile validation;
  - the database round trip;
  - the voice applied on spawn;
  - random voices only ever being selectable ones.
- **`KsTtsSpeechTests`**
  - the cooldown;
  - truncation;
  - clients playing clips in all three modes.
