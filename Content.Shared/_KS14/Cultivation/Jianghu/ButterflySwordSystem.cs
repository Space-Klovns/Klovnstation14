using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Cultivation.Jianghu;

/// <summary>
///     Butterfly swords are made in pairs. With one in each hand, each strikes faster.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ButterflySwordComponent : Component
{
    [DataField]
    public float PairedAttackRateMultiplier = 1.6f;
}

/// <summary>
///     Attack rate is worked out where the swing is predicted, so this lives in shared.
/// </summary>
public sealed partial class ButterflySwordSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private EntityQuery<ButterflySwordComponent> _butterflyQuery = default!;

    [SubscribeLocalEvent]
    private void OnGetAttackRate(Entity<ButterflySwordComponent> entity, ref GetMeleeAttackRateEvent args)
    {
        if (IsPaired(entity, args.User))
            args.Multipliers *= entity.Comp.PairedAttackRateMultiplier;
    }

    public bool IsPaired(EntityUid swordUid, EntityUid userUid)
    {
        foreach (var heldUid in _handsSystem.EnumerateHeld(userUid))
        {
            if (heldUid != swordUid && _butterflyQuery.HasComp(heldUid))
                return true;
        }

        return false;
    }
}
