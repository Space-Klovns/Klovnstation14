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

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        _sawmill = Logger.GetSawmill("ks.ivdrip.spillage");
        SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<HitscanDamageDealtEvent>(OnHitscanDamageDealt);
    }

    private void OnMeleeHit(MeleeHitEvent args)
    {
        _sawmill.Debug($"Received melee hit: weapon={ToPrettyString(args.Weapon)}, attacker={ToPrettyString(args.User)}, isHit={args.IsHit}, targets={args.HitEntities.Count}.");
        if (!args.IsHit)
        {
            _sawmill.Debug("Ignoring melee event because it is not an actual hit.");
            return;
        }

        foreach (var target in args.HitEntities)
        {
            _sawmill.Debug($"Processing melee target {ToPrettyString(target)}.");
            SpillWornDrips(target);
        }
    }

    private void OnProjectileHit(ref ProjectileHitEvent args)
    {
        _sawmill.Debug($"Received projectile hit: target={ToPrettyString(args.Target)}, damage={args.Damage}.");
        SpillWornDrips(args.Target);
    }

    private void OnHitscanDamageDealt(ref HitscanDamageDealtEvent args)
    {
        _sawmill.Debug($"Received hitscan damage: target={ToPrettyString(args.Target)}, damage={args.DamageDealt}.");
        SpillWornDrips(args.Target);
    }

    private void SpillWornDrips(EntityUid wearer)
    {
        _sawmill.Debug($"Checking equipped IV drips on {ToPrettyString(wearer)}.");
        if (!TryComp<InventoryComponent>(wearer, out _))
        {
            _sawmill.Debug($"{ToPrettyString(wearer)} has no inventory component.");
            return;
        }

        var inventorySlots = _inventorySystem.GetSlotEnumerator(wearer);
        while (inventorySlots.NextItem(out var itemUid))
        {
            _sawmill.Debug($"Inspecting equipped item {ToPrettyString(itemUid)}.");
            if (!TryComp<IvDripComponent>(itemUid, out var ivDripComponent))
            {
                _sawmill.Debug($"{ToPrettyString(itemUid)} is not an IV drip.");
                continue;
            }

            if (!ivDripComponent.SpillOnWearerAttacked)
            {
                _sawmill.Debug($"IV drip {ToPrettyString(itemUid)} has spilling disabled.");
                continue;
            }

            if (!_solutionContainerSystem.TryGetSolution(itemUid, ivDripComponent.SolutionName, out var solutionEntity, out var solution))
            {
                _sawmill.Debug($"IV drip {ToPrettyString(itemUid)} has no '{ivDripComponent.SolutionName}' solution container.");
                continue;
            }

            _sawmill.Debug($"IV drip {ToPrettyString(itemUid)} found solution entity={solutionEntity}, volume={solution.Volume}, configuredSpillAmount={ivDripComponent.SpillAmount}.");

            var spillAmount = FixedPoint2.Min(ivDripComponent.SpillAmount, solution.Volume);
            if (spillAmount <= FixedPoint2.Zero)
            {
                _sawmill.Debug($"IV drip {ToPrettyString(itemUid)} calculated a non-positive spill amount: {spillAmount}.");
                continue;
            }

            _sawmill.Debug($"Splitting {spillAmount} units from IV drip {ToPrettyString(itemUid)}.");
            var spilledSolution = _solutionContainerSystem.SplitSolution(solutionEntity.Value, spillAmount);
            var wearerCoordinates = Transform(wearer).Coordinates;
            _sawmill.Debug($"Split result volume={spilledSolution.Volume}; spilling at {wearerCoordinates}.");
            var spillSucceeded = _puddleSystem.TrySpillAt(wearerCoordinates, spilledSolution, out var puddleUid);
            _sawmill.Debug($"Puddle spill result for IV drip {ToPrettyString(itemUid)}: success={spillSucceeded}, puddle={puddleUid}.");
        }

        _sawmill.Debug($"Finished checking equipped IV drips on {ToPrettyString(wearer)}.");
    }
}
