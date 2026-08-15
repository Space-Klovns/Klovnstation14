using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server._KS14.Language; // KS14
using Content.Server._KS14.Translation; // KS14
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Speech.EntitySystems;
using Content.Server.Speech.Prototypes;
using Content.Server.Station.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Players.RateLimiting;
using Content.Shared.Radio;
using Content.Shared.Station.Components;
using Content.Shared._KS14.Language; // KS14
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

// TODO refactor whatever active warzone this class and chatmanager have become
/// <summary>
///     ChatSystem is responsible for in-simulation chat handling, such as whispering, speaking, emoting, etc.
///     ChatSystem depends on ChatManager to actually send the messages.
/// </summary>
public sealed partial class ChatSystem : SharedChatSystem
{
    [Dependency] private IReplayRecordingManager _replay = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IChatSanitizationManager _sanitizer = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ReplacementAccentSystem _wordreplacement = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private KsTranslationSystem _translation = default!; // KS14
    [Dependency] private KsLanguageSystem _ksLanguage = default!; // KS14
    [Dependency] private EntityQuery<GhostHearingComponent> _ghostHearingQuery = default!;

    private bool _loocEnabled = true;
    private bool _deadLoocEnabled;
    private bool _critLoocEnabled;
    private readonly bool _adminLoocEnabled = true;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, CCVars.LoocEnabled, OnLoocEnabledChanged, true);
        Subs.CVar(_configurationManager, CCVars.DeadLoocEnabled, OnDeadLoocEnabledChanged, true);
        Subs.CVar(_configurationManager, CCVars.CritLoocEnabled, OnCritLoocEnabledChanged, true);

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnGameChange);
    }

    private void OnLoocEnabledChanged(bool val)
    {
        if (_loocEnabled == val) return;

        _loocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-looc-chat-enabled-message" : "chat-manager-looc-chat-disabled-message"));
    }

    private void OnDeadLoocEnabledChanged(bool val)
    {
        if (_deadLoocEnabled == val) return;

        _deadLoocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-dead-looc-chat-enabled-message" : "chat-manager-dead-looc-chat-disabled-message"));
    }

    private void OnCritLoocEnabledChanged(bool val)
    {
        if (_critLoocEnabled == val)
            return;

        _critLoocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-crit-looc-chat-enabled-message" : "chat-manager-crit-looc-chat-disabled-message"));
    }

    private void OnGameChange(GameRunLevelChangedEvent ev)
    {
        switch (ev.New)
        {
            case GameRunLevel.InRound:
                if (!_configurationManager.GetCVar(CCVars.OocEnableDuringRound))
                    _configurationManager.SetCVar(CCVars.OocEnabled, false);
                break;
            case GameRunLevel.PostRound:
            case GameRunLevel.PreRoundLobby:
                if (!_configurationManager.GetCVar(CCVars.OocEnableDuringRound))
                    _configurationManager.SetCVar(CCVars.OocEnabled, true);
                break;
        }
    }

    /// <inheritdoc />
    public override void TrySendInGameICMessage(
        EntityUid source,
        string message,
        InGameICChatType desiredType,
        bool hideChat,
        bool hideLog = false,
        IConsoleShell? shell = null,
        ICommonSession? player = null,
        string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false,
        ProtoId<KsLanguagePrototype>? ksLanguageOverride = null /* KS14 */)
    {
        TrySendInGameICMessage(source, message, desiredType, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal, hideLog, shell, player, nameOverride, checkRadioPrefix, ignoreActionBlocker, ksLanguageOverride /* KS14 */);
    }

    /// <inheritdoc />
    public override void TrySendInGameICMessage(
        EntityUid source,
        string message,
        InGameICChatType desiredType,
        ChatTransmitRange range,
        bool hideLog = false,
        IConsoleShell? shell = null,
        ICommonSession? player = null,
        string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false,
        ProtoId<KsLanguagePrototype>? ksLanguageOverride = null /* KS14 */
        )
    {
        if (HasComp<GhostComponent>(source))
        {
            // Ghosts can only send dead chat messages, so we'll forward it to InGame OOC.
            TrySendInGameOOCMessage(source, message, InGameOOCChatType.Dead, range == ChatTransmitRange.HideChat, shell, player);
            return;
        }

        if (player != null && _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // Sus
        if (player?.AttachedEntity is { Valid: true } entity && source != entity)
        {
            return;
        }

        if (!CanSendInGame(ref /* KS14: made `ref` */ message, shell, player))
            return;

        ignoreActionBlocker = CheckIgnoreSpeechBlocker(source, ignoreActionBlocker);

        // this method is a disaster
        // every second i have to spend working with this code is fucking agony
        // scientists have to wonder how any of this was merged
        // coding any game admin feature that involves chat code is pure torture
        // changing even 10 lines of code feels like waterboarding myself
        // and i dont feel like vibe checking 50 code paths
        // so we set this here
        // todo free me from chat code
        if (player != null)
        {
            _chatManager.EnsurePlayer(player.UserId).AddEntity(GetNetEntity(source));
        }

        if (desiredType == InGameICChatType.Speak && message.StartsWith(LocalPrefix))
        {
            // prevent radios and remove prefix.
            checkRadioPrefix = false;
            message = message[1..];
        }

        bool shouldCapitalize = (desiredType != InGameICChatType.Emote);
        bool shouldPunctuate = _configurationManager.GetCVar(CCVars.ChatPunctuation);
        // Capitalizing the word I only happens in English, so we check language here
        bool shouldCapitalizeTheWordI = (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en")
            || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en");

        // KS14 Start
        if (player is { })
        {
            var ksEv = new KsSanitiseMessageEvent(message, player);
            RaiseLocalEvent(ref ksEv);

            message = ksEv.Message;
        }
        // KS14 End

        message = SanitizeInGameICMessage(source, message, out var emoteStr, shouldCapitalize, shouldPunctuate, shouldCapitalizeTheWordI);

        // Was there an emote in the message? If so, send it.
        if (player != null && emoteStr != message && emoteStr != null)
        {
            SendEntityEmote(source, emoteStr, range, nameOverride, ignoreActionBlocker);
        }

        // This can happen if the entire string is sanitized out.
        if (string.IsNullOrEmpty(message))
            return;

        // This message may have a radio prefix, and should then be whispered to the resolved radio channel
        if (checkRadioPrefix)
        {
            if (TryProcessRadioMessage(source, message, out var modMessage, out var channel))
            {
                SendEntityWhisper(source, modMessage, range, channel, nameOverride, hideLog, ignoreActionBlocker, ksLanguageOverride /* KS14 */);
                return;
            }
        }

        // Otherwise, send whatever type.
        switch (desiredType)
        {
            case InGameICChatType.Speak:
                SendEntitySpeak(source, message, range, nameOverride, hideLog, ignoreActionBlocker, ksLanguageOverride /* KS14 */);
                break;
            case InGameICChatType.Whisper:
                SendEntityWhisper(source, message, range, null, nameOverride, hideLog, ignoreActionBlocker, ksLanguageOverride /* KS14 */);
                break;
            case InGameICChatType.Emote:
                SendEntityEmote(source, message, range, nameOverride, hideLog: hideLog, ignoreActionBlocker: ignoreActionBlocker);
                break;
        }
    }

    /// <inheritdoc />
    public override void TrySendInGameOOCMessage(
        EntityUid source,
        string message,
        InGameOOCChatType type,
        bool hideChat,
        IConsoleShell? shell = null,
        ICommonSession? player = null
        )
    {
        if (!CanSendInGame(ref /* KS14: made `ref` */ message, shell, player))
            return;

        if (player != null && _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // It doesn't make any sense for a non-player to send in-game OOC messages, whereas non-players may be sending
        // in-game IC messages.
        if (player?.AttachedEntity is not { Valid: true } entity || source != entity)
            return;

        message = SanitizeInGameOOCMessage(message);

        var sendType = type;
        // If dead player LOOC is disabled, unless you are an admin with Moderator perms, send dead messages to dead chat
        if ((_adminManager.IsAdmin(player) && _adminManager.HasAdminFlag(player, AdminFlags.Moderator)) // Override if admin
            || _deadLoocEnabled
            || (!HasComp<GhostComponent>(source) && !_mobStateSystem.IsDead(source))) // Check that player is not dead
        {
        }
        else
            sendType = InGameOOCChatType.Dead;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        // Systems can differentiate Looc and DeadChat by type, and cancel the speak attempt if necessary.
        var ev = new InGameOocMessageAttemptEvent(player, sendType);
        RaiseLocalEvent(source, ref ev, true);
        if (ev.Cancelled)
            return;

        switch (sendType)
        {
            case InGameOOCChatType.Dead:
                SendDeadChat(source, player, message, hideChat);
                break;
            case InGameOOCChatType.Looc:
                SendLOOC(source, player, message, hideChat);
                break;
        }
    }

    #region Announcements

    /// <inheritdoc />
    public override void DispatchGlobalAnnouncement(
        string message,
        string? sender = null,
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null
        )
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToAll(ChatChannel.Radio, message, wrappedMessage, default, false, true, colorOverride);
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, Filter.Broadcast(), true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Global station announcement from {sender}: {message}");
    }

    /// <inheritdoc />
    public override void DispatchFilteredAnnouncement(
        Filter filter,
        string message,
        EntityUid? source = null,
        string? sender = null,
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrappedMessage, source ?? default, false, true, colorOverride);
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, filter, true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement from {sender}: {message}");
    }

    /// <inheritdoc />
    public override void DispatchStationAnnouncement(
        EntityUid source,
        string message,
        string? sender = null,
        bool playDefaultSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        var station = _stationSystem.GetOwningStation(source);

        if (station == null)
        {
            // you can't make a station announcement without a station
            return;
        }

        if (!TryComp<StationDataComponent>(station, out var stationDataComp)) return;

        var filter = _stationSystem.GetInStation(stationDataComp);

        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrappedMessage, source, false, true, colorOverride);

        if (playDefaultSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, filter, true, AudioParams.Default.WithVolume(-2f));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement on {station} from {sender}: {message}");
    }

    #endregion

    #region Private API

    private void SendEntitySpeak(
        EntityUid source,
        string originalMessage,
        ChatTransmitRange range,
        string? nameOverride,
        bool hideLog = false,
        bool ignoreActionBlocker = false,
        ProtoId<KsLanguagePrototype>? ksLanguageOverride = null /* KS14 */
        )
    {
        if (!_actionBlocker.CanSpeak(source) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(source, originalMessage);

        if (message.Length == 0)
            return;

        var speech = GetSpeechVerb(source, message);

        // get the entity's apparent name (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(source, Name(source));
            RaiseLocalEvent(source, nameEv);
            name = nameEv.VoiceName;
            // Check for a speech verb override
            if (nameEv.SpeechVerb != null && _prototypeManager.Resolve(nameEv.SpeechVerb, out var proto))
                speech = proto;
        }

        name = FormattedMessage.EscapeText(name);

        var verb = Loc.GetString(_random.Pick(speech.SpeechVerbStrings)); // KS14: hoisted, shared by both variants
        var wrapKey = speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message"; // KS14

        // KS14 Start: one utterance context per message; SendInVoiceRange picks per recipient.
        string wrappedMessage;
        string? ksObfuscated = null;
        string? ksWrappedObfuscated = null;
        if (_ksLanguage.TryStartUtterance(source, message, out var ksLanguage, ksLanguageOverride))
        {
            var (fontId, fontSize) = _ksLanguage.ResolveFont(ksLanguage, speech);
            wrappedMessage = Loc.GetString(wrapKey,
                ("entityName", name),
                ("verb", verb),
                ("fontType", fontId),
                ("fontSize", fontSize),
                ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(message))));

            ksObfuscated = ksLanguage.Obfuscated;
            ksWrappedObfuscated = Loc.GetString(wrapKey,
                ("entityName", name),
                ("verb", verb),
                ("fontType", fontId),
                ("fontSize", fontSize),
                ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(ksObfuscated))));
        }
        else
        {
            wrappedMessage = Loc.GetString(wrapKey,
                ("entityName", name),
                ("verb", verb),
                ("fontType", speech.FontId),
                ("fontSize", speech.FontSize),
                ("message", FormattedMessage.EscapeText(message)));
        }
        // KS14 End

        SendInVoiceRange(ChatChannel.Local, message, wrappedMessage, source, range, ksLanguage: ksLanguage, ksObfuscated: ksObfuscated, ksWrappedObfuscated: ksWrappedObfuscated /* KS14 */);

        var ev = new EntitySpokeEvent(source, message, null, null, ksLanguage /* KS14 */);
        RaiseLocalEvent(source, ev, true);

        // To avoid logging any messages sent by entities that are not players, like vendors, cloning, etc.
        // Also doesn't log if hideLog is true.
        if (!HasComp<ActorComponent>(source) || hideLog)
            return;

        if (originalMessage == message)
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {source} as {name}: {originalMessage}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {source}: {originalMessage}.");
        }
        else
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {source} as {name}, original: {originalMessage}, transformed: {message}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {source}, original: {originalMessage}, transformed: {message}.");
        }
    }

    private void SendEntityWhisper(
        EntityUid source,
        string originalMessage,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        string? nameOverride,
        bool hideLog = false,
        bool ignoreActionBlocker = false,
        ProtoId<KsLanguagePrototype>? ksLanguageOverride = null /* KS14 */
        )
    {
        if (!_actionBlocker.CanSpeak(source) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(source, FormattedMessage.RemoveMarkupOrThrow(originalMessage));
        if (message.Length == 0)
            return;

        var obfuscatedMessage = ObfuscateMessageReadability(message, 0.2f);

        // get the entity's name by visual identity (if no override provided).
        string nameIdentity = FormattedMessage.EscapeText(nameOverride ?? Identity.Name(source, EntityManager));
        // get the entity's name by voice (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(source, Name(source));
            RaiseLocalEvent(source, nameEv);
            name = nameEv.VoiceName;
        }
        name = FormattedMessage.EscapeText(name);

        var wrappedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", name), ("message", FormattedMessage.EscapeText(message)));

        var wrappedobfuscatedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", nameIdentity), ("message", FormattedMessage.EscapeText(obfuscatedMessage)));

        var wrappedUnknownMessage = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message",
            ("message", FormattedMessage.EscapeText(obfuscatedMessage)));

        // KS14 Start: whisper variants; distance fuzz stacks on top of the scramble for far tiers.
        string? ksScrambled = null;
        string? ksScrambledMuffled = null;
        string? ksWrappedScrambled = null;
        string? ksWrappedScrambledObf = null;
        string? ksWrappedScrambledUnknown = null;
        if (_ksLanguage.TryStartUtterance(source, message, out var ksLanguage, ksLanguageOverride))
        {
            // Language visual identity on the clear whisper.
            wrappedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
                ("entityName", name), ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(message))));

            ksScrambled = ksLanguage.Obfuscated;
            ksScrambledMuffled = ObfuscateMessageReadability(ksScrambled, 0.2f);
            ksWrappedScrambled = Loc.GetString("chat-manager-entity-whisper-wrap-message",
                ("entityName", name), ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(ksScrambled))));
            ksWrappedScrambledObf = Loc.GetString("chat-manager-entity-whisper-wrap-message",
                ("entityName", nameIdentity), ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(ksScrambledMuffled))));
            ksWrappedScrambledUnknown = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message",
                ("message", _ksLanguage.StyleMessage(ksLanguage, FormattedMessage.EscapeText(ksScrambledMuffled))));
        }
        // KS14 End

        // KS14 Start: per-reader translation; only the clear variant is translatable.
        KsTranslationContext? translation = null;
        _translation.TryBeginLocal(ChatChannel.Whisper, message, source, out translation);
        // KS14 End

        foreach (var (session, data) in GetRecipients(source, WhisperMuffledRange))
        {
            EntityUid listener;

            if (session.AttachedEntity is not { Valid: true } playerEntity)
                continue;
            listener = session.AttachedEntity.Value;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue; // Won't get logged to chat, and ghosts are too far away to see the pop-up, so we just won't send it to them.

            var ksUnderstands = ksLanguage == null || _ksLanguage.Understands(listener, ksLanguage); // KS14

            if (data.Range <= WhisperClearRange || data.Observer)
            {
                // KS14 Start: scrambled deliveries never carry a translation id.
                if (!ksUnderstands)
                {
                    _chatManager.ChatMessageToOne(ChatChannel.Whisper, ksScrambled!, ksWrappedScrambled!, source, false, session.Channel);
                    continue;
                }
                // KS14 End

                int? messageId = translation is { } ctx ? _translation.TryReader(message, ctx, session.Channel) : null; // KS14
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, message, wrappedMessage, source, false, session.Channel, messageId: messageId); // KS14: messageId
            }
            //If listener is too far, they only hear fragments of the message
            else if (_examineSystem.InRangeUnOccluded(source, listener, WhisperMuffledRange))
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, ksUnderstands ? obfuscatedMessage : ksScrambledMuffled!, ksUnderstands ? wrappedobfuscatedMessage : ksWrappedScrambledObf!, source, false, session.Channel); // KS14
            //If listener is too far and has no line of sight, they can't identify the whisperer's identity
            else
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, ksUnderstands ? obfuscatedMessage : ksScrambledMuffled!, ksUnderstands ? wrappedUnknownMessage : ksWrappedScrambledUnknown!, source, false, session.Channel); // KS14
        }

        // KS14: apply the per-speaker cooldown once, only if a translation was actually started.
        if (translation is { } endCtx)
            _translation.EndMessage(endCtx);

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));

        var ev = new EntitySpokeEvent(source, message, channel, obfuscatedMessage, ksLanguage /* KS14 */);
        RaiseLocalEvent(source, ev, true);
        if (!hideLog)
            if (originalMessage == message)
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {source} as {name}: {originalMessage}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {source}: {originalMessage}.");
            }
            else
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {source} as {name}, original: {originalMessage}, transformed: {message}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {source}, original: {originalMessage}, transformed: {message}.");
            }
    }

    protected override void SendEntityEmote(
        EntityUid source,
        string action,
        ChatTransmitRange range,
        string? nameOverride,
        bool hideLog = false,
        bool checkEmote = true,
        bool ignoreActionBlocker = false,
        NetUserId? author = null
        )
    {
        if (!_actionBlocker.CanEmote(source) && !ignoreActionBlocker)
            return;

        // get the entity's apparent name (if no override provided).
        var ent = Identity.Entity(source, EntityManager);
        string name = FormattedMessage.EscapeText(nameOverride ?? Name(ent));

        // Emotes use Identity.Name, since it doesn't actually involve your voice at all.
        var wrappedMessage = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", name),
            ("entity", ent),
            ("message", FormattedMessage.RemoveMarkupOrThrow(action)));

        if (checkEmote &&
            !TryEmoteChatInput(source, action))
            return;

        SendInVoiceRange(ChatChannel.Emotes, action, wrappedMessage, source, range, author);
        if (!hideLog)
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {source} as {name}: {action}");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {source}: {action}");
    }

    // ReSharper disable once InconsistentNaming
    private void SendLOOC(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var name = FormattedMessage.EscapeText(Identity.Name(source, EntityManager));

        if (_adminManager.IsAdmin(player))
        {
            if (!_adminLoocEnabled) return;
        }
        else if (!_loocEnabled) return;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        var wrappedMessage = Loc.GetString("chat-manager-entity-looc-wrap-message",
            ("entityName", name),
            ("message", FormattedMessage.EscapeText(message)));

        SendInVoiceRange(ChatChannel.LOOC, message, wrappedMessage, source, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal, player.UserId);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"LOOC from {source}: {message}");
    }

    private void SendDeadChat(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var clients = GetDeadChatClients();
        var playerName = Name(source);
        string wrappedMessage;
        if (_adminManager.IsAdmin(player))
        {
            wrappedMessage = Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                ("userName", player.Channel.UserName),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Admin dead chat from {source}: {message}");
        }
        else
        {
            wrappedMessage = Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                ("playerName", (playerName)),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Dead chat from {source}: {message}");
        }

        // KS14 Start: per-reader dead-chat translation. Dead chat is one broadcast to the dead-client list;
        // readers whose language differs from the speaker share one message id and get a targeted swap.
        var deadClients = clients.ToList();
        int? messageId = null;
        if (_translation.TryBeginSession(ChatChannel.Dead, message, player, out var translationCtx))
        {
            foreach (var client in deadClients)
                _translation.TryReaderShared(message, translationCtx, client, ref messageId);

            _translation.EndMessage(translationCtx);
        }
        // KS14 End

        _chatManager.ChatMessageToMany(ChatChannel.Dead, message, wrappedMessage, source, hideChat, true, deadClients, author: player.UserId, messageId: messageId); // KS14: messageId
    }
    #endregion

    #region Utility

    private enum MessageRangeCheckResult
    {
        Disallowed,
        HideChat,
        Full
    }

    /// <summary>
    ///     If hideChat should be set as far as replays are concerned.
    /// </summary>
    private bool MessageRangeHideChatForReplay(ChatTransmitRange range)
    {
        return range == ChatTransmitRange.HideChat;
    }

    /// <summary>
    ///     Checks if a target as returned from GetRecipients should receive the message.
    ///     Keep in mind data.Range is -1 for out of range observers.
    /// </summary>
    private MessageRangeCheckResult MessageRangeCheck(ICommonSession session, ICChatRecipientData data, ChatTransmitRange range)
    {
        var initialResult = MessageRangeCheckResult.Full;
        switch (range)
        {
            case ChatTransmitRange.Normal:
                initialResult = MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.GhostRangeLimit:
                initialResult = (data.Observer && data.Range < 0 && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.HideChat : MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.HideChat:
                initialResult = MessageRangeCheckResult.HideChat;
                break;
            case ChatTransmitRange.NoGhosts:
                initialResult = (data.Observer && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.Disallowed : MessageRangeCheckResult.Full;
                break;
        }
        var insistHideChat = data.HideChatOverride ?? false;
        var insistNoHideChat = !(data.HideChatOverride ?? true);
        if (insistHideChat && initialResult == MessageRangeCheckResult.Full)
            return MessageRangeCheckResult.HideChat;
        if (insistNoHideChat && initialResult == MessageRangeCheckResult.HideChat)
            return MessageRangeCheckResult.Full;
        return initialResult;
    }

    /// <summary>
    ///     Sends a chat message to the given players in range of the source entity.
    /// </summary>
    private void SendInVoiceRange(ChatChannel channel, string message, string wrappedMessage, EntityUid source, ChatTransmitRange range, NetUserId? author = null, KsUtteranceContext? ksLanguage = null /* KS14 */, string? ksObfuscated = null /* KS14 */, string? ksWrappedObfuscated = null /* KS14 */)
    {
        // KS14 Start: per-reader chat translation. Local and LOOC both fan out one message per reader here
        // and resolve the speaker from the entity they control.
        KsTranslationContext? translation = null;
        if (channel is ChatChannel.Local or ChatChannel.LOOC)
            _translation.TryBeginLocal(channel, message, source, out translation);
        // KS14 End

        foreach (var (session, data) in GetRecipients(source, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;
            var entHideChat = entRange == MessageRangeCheckResult.HideChat;

            // KS14 Start: non-understanders get the scrambled variant and never a translation
            // swap. Only Local is language-gated.
            if (ksLanguage != null && channel == ChatChannel.Local
                && session.AttachedEntity is { } ksListener
                && !_ksLanguage.Understands(ksListener, ksLanguage))
            {
                _chatManager.ChatMessageToOne(channel, ksObfuscated!, ksWrappedObfuscated!, source, entHideChat, session.Channel, author: author);
                continue;
            }
            // KS14 End

            // KS14: stamp a message id when this reader will receive a translation swap (skip hidden recipients).
            int? messageId = translation is { } ctx && !entHideChat
                ? _translation.TryReader(message, ctx, session.Channel)
                : null;
            _chatManager.ChatMessageToOne(channel, message, wrappedMessage, source, entHideChat, session.Channel, author: author, messageId: messageId); // KS14: messageId
        }

        // KS14: apply the per-speaker cooldown once, only if a translation was actually started.
        if (translation is { } endCtx)
            _translation.EndMessage(endCtx);

        _replay.RecordServerMessage(new ChatMessage(channel, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));
    }

    /// <summary>
    ///     Returns true if the given player is 'allowed' to send the given message, false otherwise.
    /// </summary>
    private bool CanSendInGame(ref /* KS14: made `ref` */ string message, IConsoleShell? shell = null, ICommonSession? player = null)
    {
        // Non-players don't have to worry about these restrictions.
        if (player == null)
            return true;

        var mindContainerComponent = player.ContentData()?.Mind;

        if (mindContainerComponent == null)
        {
            shell?.WriteError("You don't have a mind!");
            return false;
        }

        if (player.AttachedEntity is not { Valid: true } _)
        {
            shell?.WriteError("You don't have an entity!");
            return false;
        }

        // KS14: moved charlimit check here
        if (_chatManager.MessageCharacterLimit(player, message))
            return false;

        // KS14 Start
        var ksEv = new KsBeforeMessageSentEvent(message, false, player);
        RaiseLocalEvent(ref ksEv);

        if (ksEv.Cancelled)
            return false;

        message = ksEv.Message;
        return true;
        // KS14 End
    }

    // ReSharper disable once InconsistentNaming
    private string SanitizeInGameICMessage(EntityUid source, string message, out string? emoteStr, bool capitalize = true, bool punctuate = false, bool capitalizeTheWordI = true)
    {
        var newMessage = SanitizeMessageReplaceWords(message.Trim());

        GetRadioKeycodePrefix(source, newMessage, out newMessage, out var prefix);

        // Sanitize it first as it might change the word order
        _sanitizer.TrySanitizeEmoteShorthands(newMessage, source, out newMessage, out emoteStr);

        if (capitalize)
            newMessage = SanitizeMessageCapital(newMessage);
        if (capitalizeTheWordI)
            newMessage = SanitizeMessageCapitalizeTheWordI(newMessage, "i");
        if (punctuate)
            newMessage = SanitizeMessagePeriod(newMessage);

        return prefix + newMessage;
    }

    private string SanitizeInGameOOCMessage(string message)
    {
        var newMessage = message.Trim();
        newMessage = FormattedMessage.EscapeText(newMessage);

        return newMessage;
    }

    public string TransformSpeech(EntityUid sender, string message)
    {
        var ev = new TransformSpeechEvent(sender, message);
        RaiseLocalEvent(sender, ev, true);

        return ev.Message;
    }

    public bool CheckIgnoreSpeechBlocker(EntityUid sender, bool ignoreBlocker)
    {
        if (ignoreBlocker)
            return ignoreBlocker;

        var ev = new CheckIgnoreSpeechBlockerEvent(sender, ignoreBlocker);
        RaiseLocalEvent(sender, ev, true);

        return ev.IgnoreBlocker;
    }

    private IEnumerable<INetChannel> GetDeadChatClients()
    {
        return Filter.Empty()
            .AddWhereAttachedEntity(HasComp<GhostComponent>)
            .Recipients
            .Union(_adminManager.ActiveAdmins)
            .Select(p => p.Channel);
    }

    private string SanitizeMessagePeriod(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        // Adds a period if the last character is a letter.
        if (char.IsLetter(message[^1]))
            message += ".";
        return message;
    }

    public static readonly ProtoId<ReplacementAccentPrototype> ChatSanitize_Accent = "chatsanitize";

    public string SanitizeMessageReplaceWords(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var msg = message;

        msg = _wordreplacement.ApplyReplacements(msg, ChatSanitize_Accent);

        return msg;
    }

    /// <summary>
    ///     Returns list of players and ranges for all players withing some range. Also returns observers with a range of -1.
    /// </summary>
    private Dictionary<ICommonSession, ICChatRecipientData> GetRecipients(EntityUid source, float voiceGetRange)
    {
        // TODO proper speech occlusion

        var recipients = new Dictionary<ICommonSession, ICChatRecipientData>();

        var transformSource = Transform(source);
        var sourceMapId = transformSource.MapID;
        var sourceCoords = transformSource.Coordinates;

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is not { Valid: true } playerEntity)
                continue;

            var transformEntity = Transform(playerEntity);

            if (transformEntity.MapID != sourceMapId)
                continue;

            var observer = _ghostHearingQuery.HasComponent(playerEntity);

            // even if they are a ghost hearer, in some situations we still need the range
            if (sourceCoords.TryDistance(EntityManager, transformEntity.Coordinates, out var distance) && distance < voiceGetRange)
            {
                recipients.Add(player, new ICChatRecipientData(distance, observer));
                continue;
            }

            if (observer)
                recipients.Add(player, new ICChatRecipientData(-1, true));
        }

        RaiseLocalEvent(new ExpandICChatRecipientsEvent(source, voiceGetRange, recipients));
        return recipients;
    }

    public readonly record struct ICChatRecipientData(float Range, bool Observer, bool? HideChatOverride = null)
    {
    }

    private string ObfuscateMessageReadability(string message, float chance)
    {
        var modifiedMessage = new StringBuilder(message);

        for (var i = 0; i < message.Length; i++)
        {
            if (char.IsWhiteSpace((modifiedMessage[i])))
            {
                continue;
            }

            if (_random.Prob(1 - chance))
            {
                modifiedMessage[i] = '~';
            }
        }

        return modifiedMessage.ToString();
    }

    public string BuildGibberishString(IReadOnlyList<char> charOptions, int length)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            sb.Append(_random.Pick(charOptions));
        }
        return sb.ToString();
    }

    #endregion
}

/// <summary>
///     This event is raised before chat messages are sent out to clients. This enables some systems to send the chat
///     messages to otherwise out-of view entities (e.g. for multiple viewports from cameras).
/// </summary>
public record ExpandICChatRecipientsEvent(EntityUid Source, float VoiceRange, Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> Recipients)
{
}

// KS14 Start
/// <summary>
///     Broadcasted to check whether a message can be sent.
/// </summary>
[ByRefEvent]
public record struct KsBeforeMessageSentEvent(string Message, bool Cancelled, ICommonSession Session);
// KS14 End

// KS14 Start
/// <summary>
///     Broadcasted to mutate a message that is being sent IC or something.
/// </summary>
[ByRefEvent]
public record struct KsSanitiseMessageEvent(string Message, ICommonSession Session);
// KS14 End
