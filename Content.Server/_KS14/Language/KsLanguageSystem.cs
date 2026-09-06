using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._KS14.Translation;
using Content.Server.GameTicking;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Language;
using Content.Shared._KS14.Language.Components;
using Content.Shared.Chat;
using Content.Shared.Ghost;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Implants;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Speech;
using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Language;

/// <summary>
///     Perception kernel. One <see cref="KsUtteranceContext"/> per spoken message; every delivery
///     path asks which variant a listener receives. Null context means the vanilla path.
/// </summary>
public sealed partial class KsLanguageSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private KsTranslationSystem _translation = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>
    ///     The implicit default language, from the klovn.language.fallback cvar.
    /// </summary>
    public ProtoId<KsLanguagePrototype> FallbackLanguage { get; private set; } = "KsLangCommon";

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, KsCCVars.LanguageEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, KsCCVars.LanguageFallback, v => FallbackLanguage = v, true);

        SubscribeLocalEvent<KsLanguageKnowledgeComponent, MapInitEvent>(OnKnowledgeMapInit);
        SubscribeLocalEvent<KsLanguageGrantComponent, MapInitEvent>(OnGrantMapInit);
        SubscribeLocalEvent<KsLanguageGrantComponent, EntGotInsertedIntoContainerMessage>(OnGrantInserted);
        SubscribeLocalEvent<KsLanguageGrantComponent, EntGotRemovedFromContainerMessage>(OnGrantRemoved);
        SubscribeLocalEvent<KsLanguageGrantComponent, ItemToggledEvent>(OnGrantToggled);

        // Direct for intrinsic grants, relayed for held/worn ones; implants have no combined helper.
        Subs.SubscribeWithRelay<KsLanguageGrantComponent, KsRefreshLanguagesEvent>(ApplyGrant);
        SubscribeLocalEvent<KsLanguageGrantComponent, ImplantRelayEvent<KsRefreshLanguagesEvent>>(OnGrantRefreshImplanted);

        SubscribeNetworkEvent<KsSetLanguageMessage>(OnSetLanguageRequest);

        SubscribeLocalEvent<TriggerOnVoiceComponent, KsVoiceTriggerExaminedEvent>(OnVoiceTriggerExamined);
    }

    /// <summary>
    ///     Pushes the recorded phrase's language and, for understanders, the clear reading.
    ///     Server-side so the clear text never reaches a client that cannot read it.
    /// </summary>
    private void OnVoiceTriggerExamined(Entity<TriggerOnVoiceComponent> ent, ref KsVoiceTriggerExaminedEvent args)
    {
        if (ent.Comp.KsKeyPhraseLanguage is not { } languageId || ent.Comp.KsKeyPhraseClear is not { } clear)
            return;

        var examine = args.Examine;
        if (!examine.IsInDetailsRange || !ent.Comp.ShowExamine)
            return;

        if (!_prototypes.TryIndex(languageId, out var proto))
            return;

        examine.PushMarkup(Loc.GetString("ks-language-voice-trigger-language", ("language", proto.LocalizedName)));
        if (Understands(examine.Examiner, languageId))
            examine.PushMarkup(Loc.GetString("ks-language-voice-trigger-understood", ("keyphrase", clear)));
    }

    #region Utterances

    /// <summary>
    ///     Returns false (null context) when the system is disabled or the message is in the
    ///     default language; the caller must then run its vanilla path untouched.
    /// </summary>
    public bool TryStartUtterance(
        EntityUid source,
        string message,
        [NotNullWhen(true)] out KsUtteranceContext? ctx,
        ProtoId<KsLanguagePrototype>? forcedLanguage = null)
    {
        ctx = null;
        if (!_enabled)
            return false;

        var langId = forcedLanguage;
        if (langId == null && TryComp<KsLanguageSpeakerComponent>(source, out var speaker))
            langId = speaker.CurrentLanguage;

        if (langId is not { } id || id == FallbackLanguage)
            return false;

        if (!_prototypes.TryIndex(id, out var proto))
            return false;

        ctx = new KsUtteranceContext(proto, message, _ticker.RoundId);
        return true;
    }

    /// <summary>
    ///     Ghosts and omniglots understand everything; entities without language components know
    ///     exactly the default language.
    /// </summary>
    public bool Understands(EntityUid listener, KsUtteranceContext ctx)
        => Understands(listener, ctx.LanguageId);
    public bool Understands(EntityUid listener, ProtoId<KsLanguagePrototype> language)
    {
        if (HasComp<GhostComponent>(listener) || HasComp<KsOmniglotComponent>(listener))
            return true;

        if (!TryComp<KsLanguageSpeakerComponent>(listener, out var speaker))
            return language == FallbackLanguage;

        return speaker.Understood.Contains(language);
    }

    /// <summary>
    ///     Radio delivery seam: non-understanders get the scrambled clone, understood deliveries
    ///     go through the DeepL per-reader swap. A null clone means default language.
    /// </summary>
    public MsgChatMessage ApplyListener(
        MsgChatMessage shared,
        KsUtteranceContext? language,
        MsgChatMessage? obfuscated,
        KsTranslationContext? translation,
        ICommonSession session)
    {
        if (language != null && obfuscated != null
            && session.AttachedEntity is { } listener
            && !Understands(listener, language))
        {
            return obfuscated;
        }

        return _translation.ApplyRadioReader(shared, translation, session);
    }

    /// <summary>
    ///     Color tint and "(Name)" chip on an already-escaped body; applied to clear and
    ///     scrambled variants alike.
    /// </summary>
    public string StyleMessage(KsUtteranceContext ctx, string escapedMessage)
    {
        var body = ctx.Language.Color is { } color
            ? $"[color={color.ToHex()}]{escapedMessage}[/color]"
            : escapedMessage;

        if (!ctx.Language.ShowTag)
            return body;

        return Loc.GetString("ks-language-chat-tag", ("language", ctx.Language.LocalizedName)) + " " + body;
    }

    /// <summary>
    ///     Message-body font for wrap templates: the language's override, else the speech verb's.
    /// </summary>
    public (string FontId, int FontSize) ResolveFont(KsUtteranceContext ctx, SpeechVerbPrototype speech)
    {
        return (ctx.Language.FontId ?? speech.FontId, ctx.Language.FontSize ?? speech.FontSize);
    }

    #endregion

    #region Knowledge and grants

    public void InvalidateLanguages(EntityUid uid)
    {
        var speaker = EnsureComp<KsLanguageSpeakerComponent>(uid);
        Recompute(uid, speaker);
    }

    /// <summary>
    ///     Additive merge into intrinsic knowledge + roster recompute, so several traits compose.
    ///     First knowledge keeps the implicit station default.
    /// </summary>
    public void AddKnowledge(EntityUid uid,
        List<ProtoId<KsLanguagePrototype>> speaks,
        List<ProtoId<KsLanguagePrototype>> understands)
    {
        if (!TryComp<KsLanguageKnowledgeComponent>(uid, out var knowledge))
        {
            knowledge = AddComp<KsLanguageKnowledgeComponent>(uid);
            knowledge.Speaks.Add(FallbackLanguage);
            knowledge.Understands.Add(FallbackLanguage);
        }

        foreach (var lang in speaks)
        {
            if (!knowledge.Speaks.Contains(lang))
                knowledge.Speaks.Add(lang);
            if (!knowledge.Understands.Contains(lang))
                knowledge.Understands.Add(lang);
        }

        foreach (var lang in understands)
        {
            if (!knowledge.Understands.Contains(lang))
                knowledge.Understands.Add(lang);
        }

        InvalidateLanguages(uid);
    }

    public void SetCurrentLanguage(EntityUid uid, ProtoId<KsLanguagePrototype> language)
    {
        if (!TryComp<KsLanguageSpeakerComponent>(uid, out var speaker))
            return;

        if (!speaker.Spoken.Contains(language))
            return;

        speaker.CurrentLanguage = language;
        Dirty(uid, speaker);
    }

    private void Recompute(EntityUid uid, KsLanguageSpeakerComponent speaker)
    {
        var ev = new KsRefreshLanguagesEvent(uid, new HashSet<ProtoId<KsLanguagePrototype>>(), new HashSet<ProtoId<KsLanguagePrototype>>());

        if (TryComp<KsLanguageKnowledgeComponent>(uid, out var knowledge))
        {
            foreach (var lang in knowledge.Speaks)
                ev.Spoken.Add(lang);
            foreach (var lang in knowledge.Understands)
                ev.Understood.Add(lang);
        }
        else
        {
            // No authored knowledge = the station default, matching vanilla.
            ev.Spoken.Add(FallbackLanguage);
            ev.Understood.Add(FallbackLanguage);
        }

        RaiseLocalEvent(uid, ref ev);

        var spoken = Sorted(ev.Spoken);
        var understood = Sorted(ev.Understood);
        var current = speaker.CurrentLanguage is { } kept && spoken.Contains(kept)
            ? kept
            : spoken.Count > 0 ? spoken[0] : (ProtoId<KsLanguagePrototype>?) null;

        // Container churn re-raises refreshes constantly; only an actual roster change is dirtied.
        if (current == speaker.CurrentLanguage
            && spoken.SequenceEqual(speaker.Spoken)
            && understood.SequenceEqual(speaker.Understood))
            return;

        speaker.Spoken = spoken;
        speaker.Understood = understood;
        speaker.CurrentLanguage = current;
        Dirty(uid, speaker);
    }

    private void OnGrantRefreshImplanted(EntityUid uid, KsLanguageGrantComponent grant, ImplantRelayEvent<KsRefreshLanguagesEvent> args)
    {
        var ev = args.Args;
        ApplyGrant(uid, grant, ref ev);
    }

    private void ApplyGrant(EntityUid grantEnt, KsLanguageGrantComponent grant, ref KsRefreshLanguagesEvent ev)
    {
        if (!grant.Enabled)
            return;

        // Translator devices only work while switched on; intrinsic and implant grants have no toggle.
        if (grantEnt != ev.Holder && TryComp<ItemToggleComponent>(grantEnt, out var toggle) && !toggle.Activated)
            return;

        TryComp<KsLanguageKnowledgeComponent>(ev.Holder, out var knowledge);
        if (!RequirementsMet(grant, knowledge))
            return;

        foreach (var lang in grant.Speaks)
        {
            ev.Spoken.Add(lang);
            ev.Understood.Add(lang);
        }

        foreach (var lang in grant.Understands)
            ev.Understood.Add(lang);
    }

    private bool RequirementsMet(KsLanguageGrantComponent grant, KsLanguageKnowledgeComponent? knowledge)
    {
        if (grant.Requires.Count == 0)
            return true;

        var matched = 0;
        foreach (var required in grant.Requires)
        {
            var known = knowledge == null
                ? required == FallbackLanguage
                : knowledge.Speaks.Contains(required) || knowledge.Understands.Contains(required);

            if (!known)
                continue;

            if (!grant.RequiresAll)
                return true;

            matched++;
        }

        return grant.RequiresAll && matched == grant.Requires.Count;
    }

    private List<ProtoId<KsLanguagePrototype>> Sorted(HashSet<ProtoId<KsLanguagePrototype>> set)
    {
        var list = new List<ProtoId<KsLanguagePrototype>>(set);
        list.Sort((a, b) =>
        {
            var orderA = _prototypes.TryIndex(a, out var protoA) ? protoA.SortOrder : int.MaxValue;
            var orderB = _prototypes.TryIndex(b, out var protoB) ? protoB.SortOrder : int.MaxValue;
            var cmp = orderA.CompareTo(orderB);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
        });
        return list;
    }

    #endregion

    #region Event handlers

    private void OnKnowledgeMapInit(EntityUid uid, KsLanguageKnowledgeComponent component, MapInitEvent args)
    {
        InvalidateLanguages(uid);
    }

    private void OnGrantMapInit(EntityUid uid, KsLanguageGrantComponent component, MapInitEvent args)
    {
        // A grant directly on a beneficiary must apply even when it map-inits inside a container;
        // the component predicate excludes free-standing grant items (no cache/knowledge/hands).
        if (HasComp<KsLanguageSpeakerComponent>(uid)
            || HasComp<KsLanguageKnowledgeComponent>(uid)
            || HasComp<HandsComponent>(uid))
        {
            InvalidateLanguages(uid);
        }
    }

    private void OnGrantInserted(EntityUid uid, KsLanguageGrantComponent component, EntGotInsertedIntoContainerMessage args)
    {
        InvalidateHolder(args.Container.Owner);
    }

    private void OnGrantRemoved(EntityUid uid, KsLanguageGrantComponent component, EntGotRemovedFromContainerMessage args)
    {
        InvalidateHolder(args.Container.Owner);
    }

    private void OnGrantToggled(EntityUid uid, KsLanguageGrantComponent component, ref ItemToggledEvent args)
    {
        if (_containers.TryGetContainingContainer((uid, null, null), out var container))
            InvalidateHolder(container.Owner);
    }

    private void InvalidateHolder(EntityUid holder)
    {
        // Only beneficiaries get a speaker cache; a translator in a locker must not tag the locker.
        if (HasComp<KsLanguageSpeakerComponent>(holder)
            || HasComp<KsLanguageKnowledgeComponent>(holder)
            || HasComp<HandsComponent>(holder))
        {
            InvalidateLanguages(holder);
        }
    }

    private void OnSetLanguageRequest(KsSetLanguageMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid)
            return;

        SetCurrentLanguage(uid, msg.Language);
    }

    #endregion
}
