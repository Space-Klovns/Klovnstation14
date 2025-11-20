using Content.Shared.Actions;
using Content.Shared._KS14.Silicons.Bots.Components;
using Robust.Shared.Serialization;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Shared.Silicons.StationAi;

public abstract partial class SharedStationAiSystem
{
    private EntityUid currentTargetedBot;

    private void InitializeBot()
    {
        SubscribeLocalEvent<StationAiHeldComponent, SelectControlledBotEvent>(OnSelectBot);
        SubscribeLocalEvent<StationAiHeldComponent, MoveControlledBotToPositionEvent>(OnMoveBot);
    }

    private void OnSelectBot(EntityUid ent, StationAiHeldComponent component, SelectControlledBotEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        var target = args.Target;

        if (!HasComp<ControllableBotComponent>(target))
        {
            _popup.PopupClient($"targeting failed. entity UID of your current target: {currentTargetedBot}", target, args.Performer, PopupType.Medium);
            return;
        }

        currentTargetedBot = target;
        _popup.PopupClient($"targeting successful! entity UID of your target: {currentTargetedBot}", target, args.Performer, PopupType.Medium);
    }
    private void OnMoveBot(EntityUid ent, StationAiHeldComponent component, MoveControlledBotToPositionEvent args)
    {
        var target = args.Target;
        if (currentTargetedBot == null || (currentTargetedBot != null && !Exists(currentTargetedBot)))
        {
            _popup.PopupClient("Targeting failed. either your entity does not exist anymore, or it hasn't been selected yet.", target, args.Performer, PopupType.Medium);
            return;
        }
        _popup.PopupClient($"Targeting successful. Current bot uid: {currentTargetedBot}, target coordinates: {target}", target, args.Performer, PopupType.Medium);
        TryMoveBot(currentTargetedBot, target);
    }
    public virtual void TryMoveBot(
        EntityUid botUid,
        EntityCoordinates targetCoordinates)
    {}
}
/// <summary>
/// Invoked when the entity target action ActionSelectControlledBot is called.
/// </summary>
public sealed partial class SelectControlledBotEvent : EntityTargetActionEvent;

/// <summary>
/// Invoked when the entity target action ActionMoveControlledBot is called.
/// </summary>
public sealed partial class MoveControlledBotToPositionEvent : WorldTargetActionEvent;
