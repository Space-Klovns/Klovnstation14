using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared._KS14.Signal.SignalSpeaker.Components;
using Content.Shared.Popups;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Verbs;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Signal.SignalSpeaker.EntitySystems;

public abstract class SharedSignalSpeakerSystem : EntitySystem
{
    [Dependency] protected readonly SharedUserInterfaceSystem UserInterfaceSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SignalSpeakerComponent, GetVerbsEvent<UtilityVerb>>(OnUtilityVerb);
        SubscribeLocalEvent<SignalSpeakerComponent, ExaminedEvent>(OnExamined);
        // Bound UI subscriptions
        SubscribeLocalEvent<SignalSpeakerComponent, SignalSpeakerTextChangedMessage>(OnSignalSpeakerTextChanged);
        SubscribeLocalEvent<SignalSpeakerComponent, SignalSpeakerApplyMessage>(OnSignalSpeakerApply);
        SubscribeLocalEvent<SignalSpeakerComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<SignalSpeakerComponent, ComponentHandleState>(OnHandleState);
    }

    private void OnGetState(Entity<SignalSpeakerComponent> ent, ref ComponentGetState args)
    {
        args.State = new SignalSpeakerComponentState(ent.Comp.AssignedText)
        {
            MaxTextChars = ent.Comp.MaxTextChars,
        };
    }

    private void OnHandleState(Entity<SignalSpeakerComponent> ent, ref ComponentHandleState args)
    {
        if (args.Current is not SignalSpeakerComponentState state)
            return;

        ent.Comp.MaxTextChars = state.MaxTextChars;

        if (ent.Comp.AssignedText == state.AssignedText)
            return;

        ent.Comp.AssignedText = state.AssignedText;
        UpdateUI(ent);
    }

    protected virtual void UpdateUI(Entity<SignalSpeakerComponent> ent)
    {
    }

    private void ApplyTextToSpeaker(Entity<SignalSpeakerComponent> ent, EntityUid user)
    {
        if (ent.Comp.AssignedText == string.Empty)
            return;

        if (!TryComp<SpeakOnTriggerComponent>(ent.Owner, out var speakComp))
        {
            _popupSystem.PopupClient(Loc.GetString("signal-speaker-no-trigger"), user, user);
            return;
        }

        speakComp.NonLocText = ent.Comp.AssignedText;
        Dirty(ent.Owner, speakComp);

        _popupSystem.PopupClient(Loc.GetString("signal-speaker-successfully-applied"), user, user);

        // Log
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} set speak text on {ToPrettyString(ent):signalspeaker}");
    }

    private void OnUtilityVerb(Entity<SignalSpeakerComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (args.Target is not { Valid: true } target || !args.CanAccess)
            return;

        var user = args.User;

        var applyVerb = new UtilityVerb()
        {
            Act = () =>
            {
                ApplyTextToSpeaker(ent, user);
            },
            Text = Loc.GetString("signal-speaker-apply-text")
        };

        args.Verbs.Add(applyVerb);
    }

    private void OnSignalSpeakerTextChanged(EntityUid uid, SignalSpeakerComponent signalSpeaker, SignalSpeakerTextChangedMessage args)
    {
        var text = args.Text.Trim();
        signalSpeaker.AssignedText = text[..Math.Min(signalSpeaker.MaxTextChars, text.Length)];
        UpdateUI((uid, signalSpeaker));
        Dirty(uid, signalSpeaker);

        // Log text change
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.Actor):user} set {ToPrettyString(uid):signalspeaker} to apply text \"{signalSpeaker.AssignedText}\"");
    }

    private void OnSignalSpeakerApply(EntityUid uid, SignalSpeakerComponent signalSpeaker, SignalSpeakerApplyMessage args)
    {
        ApplyTextToSpeaker((uid, signalSpeaker), args.Actor);
    }

    private void OnExamined(Entity<SignalSpeakerComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var text = ent.Comp.AssignedText == string.Empty
            ? Loc.GetString("signal-speaker-examine-blank")
            : Loc.GetString("signal-speaker-examine-text", ("text", ent.Comp.AssignedText));
        args.PushMarkup(text);
    }
}
