using Content.Shared._KS14.Atmos.Components;
using Content.Shared._KS14.BatteryShielding;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Atmos.EntitySystems;

public sealed partial class KsGasMaxPressureIntervalSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsGasMaxPressureIntervalComponent, KsGasMaxPressureAttemptLoseIntegrityEvent>(OnAttemptLoseIntegrity, after: [typeof(SharedBatteryShieldingSystem)]);
    }

    private void OnAttemptLoseIntegrity(Entity<KsGasMaxPressureIntervalComponent> entity, ref KsGasMaxPressureAttemptLoseIntegrityEvent args)
    {
        if (args.Cancelled)
            return;

        if (_gameTiming.CurTime < entity.Comp.NextUpdate)
        {
            args.Cancelled = true;
            return;
        }

        entity.Comp.NextUpdate = _gameTiming.CurTime + entity.Comp.Interval;
        Dirty(entity);

        foreach (var (integrityThreshold, locId) in entity.Comp.PopupLocs)
        {
            if (integrityThreshold <= args.Component.Integrity)
                continue;

            if (locId is not { })
                break;

            _popupSystem.PopupEntity(Loc.GetString(locId, ("name", Name(entity.Owner))), entity.Owner, type: entity.Comp.PopupType);
            break;
        }
    }
}
