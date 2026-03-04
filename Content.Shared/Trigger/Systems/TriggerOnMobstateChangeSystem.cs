using Content.Shared.Implants;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Trigger.Components.Triggers;
//KS14 START
using Content.Shared._KS14.Clothing.EntitySystems;
using Content.Shared._KS14.Clothing.Components;
//KS14 END

namespace Content.Shared.Trigger.Systems;

public sealed partial class TriggerOnMobstateChangeSystem : TriggerOnXSystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, SuicideEvent>(OnSuicide);

        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ImplantRelayEvent<MobStateChangedEvent>>(OnMobStateRelay);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ImplantRelayEvent<SuicideEvent>>(OnSuicideRelay);

        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ClothingRelayEvent<MobStateChangedEvent>>(OnMobStateClothingRelay); //KS14
    }

    private void OnMobStateChanged(EntityUid uid, TriggerOnMobstateChangeComponent component, MobStateChangedEvent args)
    {
        if (!component.MobState.Contains(args.NewMobState))
            return;

        Trigger.Trigger(uid, component.TargetMobstateEntity ? uid : args.Origin, component.KeyOut);
    }

    private void OnMobStateRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ImplantRelayEvent<MobStateChangedEvent> args)
    {
        if (!component.MobState.Contains(args.Event.NewMobState))
            return;

        Trigger.Trigger(uid, component.TargetMobstateEntity ? args.ImplantedEntity : args.Event.Origin, component.KeyOut);
    }

    //KS14 START
    private void OnMobStateClothingRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ClothingRelayEvent<MobStateChangedEvent> args)
    {
        if (!component.MobState.Contains(args.Event.NewMobState))
            return;

        Trigger.Trigger(uid, component.TargetMobstateEntity ? args.WearerEntity : args.Event.Origin, component.KeyOut);
    }
    //KS14 END

    /// <summary>
    /// Checks if the user has any implants that prevent suicide to avoid some cheesy strategies
    /// Prevents suicide by handling the event without killing the user
    /// TODO: This doesn't seem to work at the moment as the event is never checked for being handled.
    /// </summary>
    private void OnSuicide(EntityUid uid, TriggerOnMobstateChangeComponent component, SuicideEvent args)
    {
        if (args.Handled)
            return;

        if (!component.PreventSuicide)
            return;

        _popup.PopupClient(Loc.GetString("suicide-prevented"), args.Victim);
        args.Handled = true;
    }

    private void OnSuicideRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ImplantRelayEvent<SuicideEvent> args)
    {
        if (args.Event.Handled)
            return;

        if (!component.PreventSuicide)
            return;

        _popup.PopupClient(Loc.GetString("suicide-prevented"), args.Event.Victim);
        args.Event.Handled = true;
    }
}
