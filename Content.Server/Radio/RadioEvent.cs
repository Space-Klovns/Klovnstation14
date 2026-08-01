using Content.Server._KS14.Translation; // KS14
using Content.Shared._KS14.Language; // KS14
using Content.Shared.Chat;
using Content.Shared.Radio;

namespace Content.Server.Radio;

// KS14: Translation is the per-broadcast DeepL gating context (null when not eligible).
// KsLanguage is the utterance's language context (null for the default language);
// KsObfuscatedChatMsg the per-broadcast scrambled clone (for jam substitutions: garbled from the
// scrambled text, never the clear). Receiver handlers pass all three through ApplyListener.
[ByRefEvent]
public readonly record struct RadioReceiveEvent(string Message, EntityUid MessageSource, RadioChannelPrototype Channel, EntityUid RadioSource, MsgChatMessage ChatMsg, KsTranslationContext? Translation = null /* KS14 */, KsUtteranceContext? KsLanguage = null /* KS14 */, MsgChatMessage? KsObfuscatedChatMsg = null /* KS14 */);

/// <summary>
/// Event raised on the parent entity of a headset radio when a radio message is received
/// </summary>
[ByRefEvent]
public readonly record struct HeadsetRadioReceiveRelayEvent(RadioReceiveEvent RelayedEvent);

/// <summary>
/// Use this event to cancel sending message per receiver
/// </summary>
[ByRefEvent]
public record struct RadioReceiveAttemptEvent(RadioChannelPrototype Channel, EntityUid RadioSource, EntityUid RadioReceiver, MsgChatMessage OriginalChatMessage /* KS14 */, string? KsObfuscatedMessage = null /* KS14 */)
{
    public readonly RadioChannelPrototype Channel = Channel;
    public readonly EntityUid RadioSource = RadioSource;
    public readonly EntityUid RadioReceiver = RadioReceiver;
    public bool Cancelled = false;

    public MsgChatMessage? NewChatMessage = null; // KS14

    // KS14: non-understander substitute, garbled from KsObfuscatedMessage, never from the clear.
    public MsgChatMessage? KsNewObfuscatedChatMessage = null;
}

/// <summary>
/// Use this event to cancel sending message to every receiver
/// </summary>
[ByRefEvent]
public record struct RadioSendAttemptEvent(RadioChannelPrototype Channel, EntityUid RadioSource)
{
    public readonly RadioChannelPrototype Channel = Channel;
    public readonly EntityUid RadioSource = RadioSource;
    public bool Cancelled = false;
}
