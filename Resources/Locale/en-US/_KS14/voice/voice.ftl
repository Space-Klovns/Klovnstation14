## Voice chat

# Link window
ks-voice-window-title = Voice chat
ks-voice-window-explanation =
    To talk, open your personal voice link in a web browser and allow microphone access, then hold your push-to-talk key in-game.
    You don't need to do anything to hear other players.
ks-voice-window-warning = This link lets anyone who has it talk as you. Don't share it or show it on stream.
ks-voice-window-loading = Getting your voice link...
ks-voice-window-open = Open in browser
ks-voice-window-copy = Copy link
ks-voice-window-copied = Copied!
ks-voice-window-reset = Reset link
ks-voice-window-reset-tooltip = Makes a new link and disconnects any page opened with the old one.
ks-voice-window-connected = Microphone page connected.
ks-voice-window-disconnected = No microphone page connected.
ks-voice-window-keybind = Push-to-talk key: {$key}
ks-voice-window-voice-activation = Voice activation is on: no key needed.

ks-voice-link-error-disabled = Voice chat is disabled on this server.
ks-voice-link-error-no-url = This server hasn't configured a public address for voice chat.
ks-voice-link-error-reset-cooldown = Link NOT reset: wait a few seconds and try again.

# Options
ks-ui-options-voice-header = Voice chat
ks-ui-options-voice-hear = Hear other players' voices
ks-ui-options-voice-activation = Voice activation (talk without holding push-to-talk)
ks-ui-options-voice-activation-tooltip = You're heard whenever your microphone page picks up sound above its noise gate. Set the gate on that page so it doesn't pick up background noise.
ks-ui-options-voice-volume = Voice volume
ks-ui-options-voice-jitter = Voice buffer (ms)
ks-ui-options-voice-open = Set up microphone...

# Keybinds
ui-options-function-ks-voice-push-to-talk = Voice chat push-to-talk

# Verbs
ks-voice-verb-mute-local = Mute voice
ks-voice-verb-unmute-local = Unmute voice
ks-voice-verb-mute-round = Voice mute (round)
ks-voice-verb-unmute = Voice unmute

# Popups
ks-voice-popup-cooldown = You've been talking too long. Take a breath.
ks-voice-popup-muted = Your voice chat has been muted by an admin.
ks-voice-popup-auto-muted = Your voice chat was muted automatically: your audio was far too loud.

# Moderation
ks-voice-admin-alert-auto-muted = {$player} was automatically voice-muted for {$seconds}s for abusive audio levels.
ks-voice-mute-by-server = Server
ks-voice-mute-reason-none = No reason given
ks-voice-mute-reason-verb = Muted via admin verb
ks-voice-mute-length-round = rest of round
ks-voice-mute-length-minutes = {$minutes} {$minutes ->
    [one] minute
   *[other] minutes
} left

# Client command
cmd-voicechat-desc = Opens the voice chat window, with your personal microphone link.
cmd-voicechat-help = voicechat

cmd-voicelink-desc = Opens your personal voice chat page in the browser.
cmd-voicelink-help = voicelink

# Admin commands
cmd-vcmute-desc = Mutes a player's voice chat.
cmd-vcmute-help = vcmute <player> [minutes, 0 = rest of round] [reason...]
cmd-vcmute-invalid-args = Expected at least a player name.
cmd-vcmute-no-player = No online player named {$player}.
cmd-vcmute-invalid-minutes = Minutes must be a number from 0 (rest of round) to 525600.
cmd-vcmute-success = Voice-muted {$player}.
cmd-vcmute-player-completion = <player>
cmd-vcmute-minutes-completion = [minutes, 0 = rest of round]
cmd-vcmute-reason-completion = [reason]

cmd-vcunmute-desc = Lifts a player's voice chat mute.
cmd-vcunmute-help = vcunmute <player>
cmd-vcunmute-invalid-args = Expected exactly one player name.
cmd-vcunmute-success = Voice-unmuted {$player}.
cmd-vcunmute-not-muted = {$player} isn't voice-muted.

cmd-vcmutes-desc = Lists active voice chat mutes.
cmd-vcmutes-help = vcmutes
cmd-vcmutes-none = Nobody is voice-muted.
cmd-vcmutes-entry = {$player}: {$length}, by {$admin}: {$reason}
cmd-vcmutes-entry-auto = {$player}: automatically muted for abusive audio, {$seconds}s left (vcunmute lifts it)
