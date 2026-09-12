using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Inventory;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     Spills configured IV drips when their wearer receives damage through the inventory relay.
/// </summary>
public sealed partial class IvDripSpillageSystem : EntitySystem
{
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IvDripComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnWearerDamageModify);
    }

    private static bool HasPositiveBruteDamage(DamageSpecifier damage)
    {
        return (damage.DamageDict.TryGetValue("Blunt", out var bluntDamage) && bluntDamage > FixedPoint2.Zero) ||
               (damage.DamageDict.TryGetValue("Slash", out var slashDamage) && slashDamage > FixedPoint2.Zero) ||
               (damage.DamageDict.TryGetValue("Piercing", out var piercingDamage) && piercingDamage > FixedPoint2.Zero);
    }

    private void OnWearerDamageModify(Entity<IvDripComponent> entity, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (!entity.Comp.SpillOnWearerAttacked || !HasPositiveBruteDamage(args.Args.Damage) ||
            !_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var solutionEntity,
                out var solution))
        {
            return;
        }

        var spillAmount = FixedPoint2.Min(entity.Comp.SpillAmount, solution.Volume);
        if (spillAmount <= FixedPoint2.Zero)
            return;

        var spilledSolution = _solutionContainerSystem.SplitSolution(solutionEntity.Value, spillAmount);
        _puddleSystem.TrySpillAt(Transform(args.Owner).Coordinates, spilledSolution, out _);
    }
}
