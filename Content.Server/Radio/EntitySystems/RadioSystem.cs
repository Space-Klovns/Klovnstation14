using Content.Server._KS14.Language; // KS14
using Content.Server._KS14.Translation; // KS14
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared._KS14.Language; // KS14
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     This system handles intrinsic radios and the general process of converting radio messages into chat messages.
/// </summary>
public sealed partial class RadioSystem : EntitySystem
{
    [Dependency] private INetManager _netMan = default!;
    [Dependency] private IReplayRecordingManager _replay = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private KsTranslationSystem _translation = default!; // KS14
    [Dependency] private KsLanguageSystem _ksLanguage = default!; // KS14
    [Dependency] private EntityQuery<TelecomExemptComponent> _exemptQuery = default!;

    // set used to prevent radio feedback loops.
    private readonly HashSet<string> _messages = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IntrinsicRadioReceiverComponent, RadioReceiveEvent>(OnIntrinsicReceive);
        SubscribeLocalEvent<IntrinsicRadioTransmitterComponent, EntitySpokeEvent>(OnIntrinsicSpeak);
    }

    private void OnIntrinsicSpeak(EntityUid uid, IntrinsicRadioTransmitterComponent component, EntitySpokeEvent args)
    {
        if (args.Channel != null && component.Channels.Contains(args.Channel.ID))
        {
            SendRadioMessage(uid, args.Message, args.Channel, uid, ksLanguage: args.KsLanguage /* KS14 */);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    private void OnIntrinsicReceive(EntityUid uid, IntrinsicRadioReceiverComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(uid, out ActorComponent? actor))
            _netMan.ServerSendMessage(_ksLanguage.ApplyListener(args.ChatMsg, args.KsLanguage, args.KsObfuscatedChatMsg, args.Translation, actor.PlayerSession), actor.PlayerSession.Channel); // KS14
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    public void SendRadioMessage(EntityUid messageSource, string message, ProtoId<RadioChannelPrototype> channel, EntityUid radioSource, bool escapeMarkup = true, KsUtteranceContext? ksLanguage = null /* KS14 */)
    {
        SendRadioMessage(messageSource, message, _prototype.Index(channel), radioSource, escapeMarkup: escapeMarkup, ksLanguage: ksLanguage /* KS14 */);
    }

    /// <summary>
    /// Send radio message to all active radio listeners
    /// </summary>
    /// <param name="messageSource">Entity that spoke the message</param>
    /// <param name="radioSource">Entity that picked up the message and will send it, e.g. headset</param>
    public void SendRadioMessage(EntityUid messageSource, string message, RadioChannelPrototype channel, EntityUid radioSource, bool escapeMarkup = true, KsUtteranceContext? ksLanguage = null /* KS14 */)
    {
        // TODO if radios ever garble / modify messages, feedback-prevention needs to be handled better than this.
        if (!_messages.Add(message))
            return;

        // KS14 Start: resolve the language if the caller didn't thread one, so device-fed
        // microphones can't launder exotic speech onto radio clear.
        if (ksLanguage == null)
            _ksLanguage.TryStartUtterance(messageSource, message, out ksLanguage);

        if (ksLanguage != null && !ksLanguage.Language.AllowRadio)
        {
            _messages.Remove(message);
            return;
        }
        // KS14 End

        var evt = new TransformSpeakerNameEvent(messageSource, MetaData(messageSource).EntityName);
        RaiseLocalEvent(messageSource, evt);

        var name = evt.VoiceName;
        name = FormattedMessage.EscapeText(name);

        SpeechVerbPrototype speech;
        if (evt.SpeechVerb != null && _prototype.Resolve(evt.SpeechVerb, out var evntProto))
            speech = evntProto;
        else
            speech = _chat.GetSpeechVerb(messageSource, message);

        var content = escapeMarkup
            ? FormattedMessage.EscapeText(message)
            : message;

        var verb = Loc.GetString(_random.Pick(speech.SpeechVerbStrings)); // KS14: hoisted, shared by both variants
        var radioWrapKey = speech.Bold ? "chat-radio-message-wrap-bold" : "chat-radio-message-wrap"; // KS14: hoisted, shared by both variants

        // KS14 Start: language visual identity on the clear line.
        var (fontId, fontSize) = ksLanguage != null
            ? _ksLanguage.ResolveFont(ksLanguage, speech)
            : (speech.FontId, speech.FontSize);
        if (ksLanguage != null)
            content = _ksLanguage.StyleMessage(ksLanguage, content);
        // KS14 End

        var wrappedMessage = Loc.GetString(radioWrapKey, // KS14
            ("color", channel.Color),
            ("fontType", fontId), // KS14
            ("fontSize", fontSize), // KS14
            ("verb", verb), // KS14
            ("channel", $"\\[{channel.LocalizedName}\\]"),
            ("name", name),
            ("message", content));

        // most radios are relayed to chat, so lets parse the chat message beforehand
        var chat = new ChatMessage(
            ChatChannel.Radio,
            message,
            wrappedMessage,
            NetEntity.Invalid,
            null);
        var chatMsg = new MsgChatMessage { Message = chat };

        // KS14 Start: scramble the text actually broadcast; a microphone may relay a fuzzed variant.
        MsgChatMessage? ksObfuscatedChatMsg = null;
        if (ksLanguage != null)
        {
            var scrambled = ksLanguage.ObfuscateText(message);
            var wrappedScrambled = Loc.GetString(radioWrapKey,
                ("color", channel.Color),
                ("fontType", fontId),
                ("fontSize", fontSize),
                ("verb", verb),
                ("channel", $"\\[{channel.LocalizedName}\\]"),
                ("name", name),
                ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(scrambled))));

            ksObfuscatedChatMsg = new MsgChatMessage
            {
                Message = new ChatMessage(ChatChannel.Radio, scrambled, wrappedScrambled, NetEntity.Invalid, null),
            };
        }
        // KS14 End

        // KS14: begin per-reader translation once for the whole broadcast (gating + cooldown are message-level).
        _translation.TryBeginLocal(ChatChannel.Radio, message, messageSource, out var translation);

        var ev = new RadioReceiveEvent(message, messageSource, channel, radioSource, chatMsg, translation /* KS14 */, ksLanguage /* KS14 */, ksObfuscatedChatMsg /* KS14 */);

        var sendAttemptEv = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttemptEv);
        RaiseLocalEvent(radioSource, ref sendAttemptEv);
        var canSend = !sendAttemptEv.Cancelled;

        var sourceMapId = Transform(radioSource).MapID;
        var hasActiveServer = HasActiveServer(sourceMapId, channel.ID);
        var sourceServerExempt = _exemptQuery.HasComp(radioSource);

        var radioQuery = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        while (canSend && radioQuery.MoveNext(out var receiver, out var radio, out var transform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID) || (TryComp<IntercomComponent>(receiver, out var intercom) &&
                                                             !intercom.SupportedChannels.Contains(channel.ID)))
                    continue;
            }

            if (!channel.LongRange && transform.MapID != sourceMapId && !radio.GlobalReceive)
                continue;

            // don't need telecom server for long range channels or handheld radios and intercoms
            var needServer = !channel.LongRange && !sourceServerExempt;
            if (needServer && !hasActiveServer)
                continue;

            // check if message can be sent to specific receiver
            var attemptEv = new RadioReceiveAttemptEvent(channel, radioSource, receiver, chatMsg /* KS14 */, ksObfuscatedChatMsg?.Message.Message /* KS14 */);
            RaiseLocalEvent(ref attemptEv);
            RaiseLocalEvent(receiver, ref attemptEv);
            if (attemptEv.Cancelled)
                continue;

            // KS14 Start: jam substitution swaps in the jammer-built clones.
            var sentEv = attemptEv.NewChatMessage is { } newChatMsg ?
                new RadioReceiveEvent(message, messageSource, channel, radioSource, newChatMsg, translation, ksLanguage, attemptEv.KsNewObfuscatedChatMessage) :
                ev;
            // KS14 End

            // send the message
            RaiseLocalEvent(receiver, ref sentEv);
        }

        // KS14: apply the per-speaker cooldown once, only if some reader actually started a real API call.
        if (translation is { } endCtx)
            _translation.EndMessage(endCtx);

        if (name != Name(messageSource))
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} as {name} on {channel.LocalizedName}: {message}");
        else
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Radio message from {ToPrettyString(messageSource):user} on {channel.LocalizedName}: {message}");

        _replay.RecordServerMessage(chat);
        _messages.Remove(message);
    }

    /// <inheritdoc cref="TelecomServerComponent"/>
    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQuery<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        foreach (var (_, keys, power, transform) in servers)
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                keys.Channels.Contains(channelId))
            {
                return true;
            }
        }
        return false;
    }
}
