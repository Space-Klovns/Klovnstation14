using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Cultivation.Systems;

/// <summary>
///     The simple refusals of cultivation's own buttons, checked before an action runs, on the client as well as the
///         server: a technique you can't afford the qi for, or forging with nothing banked. The client refuses at
///         once with its popup instead of waiting on the server; the server makes the same check, so nothing changes
///         for anyone else. What a technique actually does still runs on the server.
/// </summary>
public sealed partial class CultivationActionPredictionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedBodyCultivationSystem _bodySystem = default!;
    [Dependency] private EntityQuery<CultivatorComponent> _cultivatorQuery = default!;
    [Dependency] private EntityQuery<BodyCultivatorComponent> _bodyCultivatorQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActionComponent, ActionAttemptEvent>(OnActionAttempt);
    }

    private void OnActionAttempt(Entity<ActionComponent> action, ref ActionAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var user = args.User;
        switch (_actionsSystem.GetEvent(action))
        {
            case IQiCostEvent qiCost when qiCost.QiCost > 0f && _cultivatorQuery.TryComp(user, out var cultivator) && cultivator.Qi < qiCost.QiCost:
                args.Cancelled = true;
                Refuse(user, "cultivation-not-enough-qi");
                break;
            // As the server checks: a part left to forge, but nothing banked to forge it with.
            case BodyForgeActionEvent when _bodyCultivatorQuery.TryComp(user, out var body) && body.Tempering < 1f
                                           && _bodySystem.WeakestGroup(body) != null:
                args.Cancelled = true;
                Refuse(user, "body-forge-no-tempering");
                break;
        }
    }

    private void Refuse(EntityUid user, string message)
    {
        if (_gameTiming.IsFirstTimePredicted)
            _popupSystem.PopupClient(Loc.GetString(message), user, user, PopupType.SmallCaution);
    }
}
