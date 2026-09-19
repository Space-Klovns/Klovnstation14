using Content.Shared._KS14.Medical.IvDrip;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Medical.IvDrip;

/// <summary>
///     Spills a worn IV drip over the floor when its wearer takes a hit hard enough to tear the line out.
/// </summary>
/// <remarks>
///     Server-only: <see cref="SharedPuddleSystem.TrySpillAt"/> has no client implementation, so running
///         this shared would split the solution out of the drip on the client and then draw nothing,
///         which snaps back on the next state.
///     Hooked to <see cref="DamageDealtEvent"/> rather than <c>DamageModifyEvent</c>. The latter is the
///         armour hook: it is raised before the damage is known to land, is skipped entirely when damage
///         ignores resistances, and fires at full strength even when armour goes on to absorb the lot.
/// </remarks>
public sealed partial class IvDripSpillageSystem : EntitySystem
{
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private EntityQuery<IvDripComponent> _ivDripQuery = default!;

    /// <summary>
    ///     Spills every drip the damaged entity is wearing that is configured to spill and that the
    ///         damage qualifies for.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnWearerDamageDealt(Entity<IvDripWearerComponent> entity, ref DamageDealtEvent args)
    {
        foreach (var dripUid in entity.Comp.DripUids)
        {
            if (!_ivDripQuery.TryComp(dripUid, out var ivDripComponent) || !ivDripComponent.SpillOnWearerAttacked)
                continue;

            if (!DealtAnySpillDamage(args.Damage, ivDripComponent.SpillDamageTypes))
                continue;

            Spill((dripUid, ivDripComponent), entity.Owner);
        }
    }

    /// <summary>
    ///     Whether the hit landed any of the damage types that tear a drip's line out.
    /// </summary>
    private static bool DealtAnySpillDamage(DamageSpecifier damage, List<ProtoId<DamageTypePrototype>> spillDamageTypes)
    {
        foreach (var damageType in spillDamageTypes)
        {
            if (damage.DamageDict.TryGetValue(damageType, out var typeDamage) && typeDamage > FixedPoint2.Zero)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Dumps up to <see cref="IvDripComponent.SpillAmount"/> out of the drip, at the wearer's feet.
    /// </summary>
    private void Spill(Entity<IvDripComponent> entity, EntityUid wearerUid)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var solutionEntity, out var solution))
            return;

        var spillAmount = FixedPoint2.Min(entity.Comp.SpillAmount, solution.Volume);
        if (spillAmount <= FixedPoint2.Zero)
            return;

        var spilledSolution = _solutionContainerSystem.SplitSolution(solutionEntity.Value, spillAmount);
        _puddleSystem.TrySpillAt(Transform(wearerUid).Coordinates, spilledSolution, out _);
    }
}
