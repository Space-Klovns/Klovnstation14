using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     Spills configured IV drips when their wearer is struck by a melee, projectile, or hitscan attack.
/// </summary>
public sealed partial class IvDripSpillageSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private SharedPuddleSystem _puddleSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<HitscanDamageDealtEvent>(OnHitscanDamageDealt);
    }

    private void OnMeleeHit(MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var target in args.HitEntities)
        {
            SpillWornDrips(target);
        }
    }

    private void OnProjectileHit(ref ProjectileHitEvent args)
    {
        SpillWornDrips(args.Target);
    }

    private void OnHitscanDamageDealt(ref HitscanDamageDealtEvent args)
    {
        SpillWornDrips(args.Target);
    }

    private void SpillWornDrips(EntityUid wearer)
    {
        var inventorySlots = _inventorySystem.GetSlotEnumerator(wearer);
        while (inventorySlots.NextItem(out var itemUid))
        {
            if (!TryComp<IvDripComponent>(itemUid, out var ivDripComponent) || !ivDripComponent.SpillOnWearerAttacked ||
                !_solutionContainerSystem.TryGetSolution(itemUid, ivDripComponent.SolutionName, out var solutionEntity, out var solution))
                continue;

            var spillAmount = FixedPoint2.Min(ivDripComponent.SpillAmount, solution.Volume);
            if (spillAmount <= FixedPoint2.Zero)
                continue;

            var spilledSolution = _solutionContainerSystem.SplitSolution(solutionEntity.Value, spillAmount);
            _puddleSystem.TrySpillAt(Transform(itemUid).Coordinates, spilledSolution, out _);
        }
    }
}