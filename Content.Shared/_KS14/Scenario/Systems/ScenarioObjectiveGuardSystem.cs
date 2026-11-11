using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Trigger.Systems;

namespace Content.Shared._KS14.Scenario.Systems;

/// <summary>
///     A faction can't capture its own objective. Starting a capture is what activating an objective does, and the
///         capture of an objective always goes to some faction other than its owner, so a member doing it would only
///         hand the round to the enemy.
/// </summary>
public sealed partial class ScenarioObjectiveGuardSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<ScenarioFactionMemberComponent> _memberQuery = default!;

    [SubscribeLocalEvent(before: [typeof(TriggerSystem)])]
    private void OnActivate(Entity<ScenarioObjectiveComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled ||
            !_memberQuery.TryComp(args.User, out var memberComponent) ||
            memberComponent.Id != entity.Comp.FactionId)
            return;

        args.Handled = true;
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-scenario-objective-popup-own"), entity.Owner, args.User);
    }
}
