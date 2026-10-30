using Content.Shared._KS14.Fluids.Components;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Fluids.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Fluids;

public sealed partial class KsGunSpraySafetySystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    [Dependency] private EntityQuery<SpraySafetyComponent> _spraySafetyQuery = default!;

    [SubscribeLocalEvent]
    private void OnShotAttempted(Entity<KsGunSpraySafetyComponent> entity, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled ||
            !_spraySafetyQuery.TryComp(entity, out var spraySafetyComponent) ||
            _itemToggleSystem.IsActivated(entity.Owner))
            return;

        args.Cancel();

        var curTime = _gameTiming.CurTime;
        if (curTime < entity.Comp.NextPopupTime)
            return;

        entity.Comp.NextPopupTime = curTime + entity.Comp.PopupCooldown;
        _popupSystem.PopupEntity(KsPopupMessage.Create(spraySafetyComponent.Popup), entity.Owner, args.User);
    }
}
