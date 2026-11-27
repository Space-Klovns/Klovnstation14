using Content.Shared._KS14.LockerMech;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.LockerMech;

/// <summary>
/// Runs the tension belt wind-up and applies impact on the server. Ordinary piloting
/// continues through the shared mech system and its existing movement prediction.
/// </summary>
public sealed partial class KsLockerMechSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedMechSystem _mechs = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private const float TipRange = 1.5f;
    private static readonly TimeSpan WindUp = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Recovery = TimeSpan.FromSeconds(2.2);
    private static readonly TimeSpan VictimKnockdown = TimeSpan.FromSeconds(3);
    private static readonly DamageSpecifier Impact = new() { DamageDict = { ["Blunt"] = 12 } };
    private static readonly DamageSpecifier Miss = new() { DamageDict = { ["Blunt"] = 8 } };

    public override void Initialize()
    {
        SubscribeLocalEvent<KsLockerMechComponent, KsLockerTipActionEvent>(OnTip);
        SubscribeLocalEvent<KsLockerMechComponent, KsLockerTipDoAfterEvent>(OnTipFinished);
    }

    private void OnTip(Entity<KsLockerMechComponent> entity, ref KsLockerTipActionEvent args)
    {
        if (args.Handled || !CanTip(entity, args.Performer, args.Target, out _, out var landing))
            return;

        // The pilot is inside a moving container. Track the chassis as the do-after target too.
        var attempt = new DoAfterArgs(EntityManager, args.Performer, WindUp,
            new KsLockerTipDoAfterEvent(GetNetCoordinates(landing)), entity, target: entity)
        {
            BreakOnMove = true,
            MovementThreshold = 0.1f,
        };
        if (!_doAfter.TryStartDoAfter(attempt))
            return;

        args.Handled = true;
        _popup.PopupEntity(KsPopupMessage.Create("ks-locker-mech-tip-warning", ("mech", entity.Owner)), entity.Owner);
    }

    private void OnTipFinished(Entity<KsLockerMechComponent> entity, ref KsLockerTipDoAfterEvent args)
    {
        // Revalidate against the live map. The pilot, cell, and landing tile can change during the wind-up.
        if (args.Cancelled || args.Handled ||
            !CanTip(entity, args.User, GetCoordinates(args.Landing), out var mech, out var landing) ||
            !_mechs.TryChangeEnergy(entity, -5, mech))
        {
            return;
        }

        args.Handled = true;
        var victims = _lookup.GetEntitiesInRange(landing, 0.4f);
        _transform.SetCoordinates(entity.Owner, landing);
        _stun.TryAddStunDuration(args.User, Recovery);

        var hit = false;
        foreach (var victim in victims)
        {
            if (victim == args.User || !HasComp<MobStateComponent>(victim))
                continue;

            hit = true;
            _damage.ChangeDamage(victim, Impact);
            _stun.TryKnockdown(victim, VictimKnockdown);
        }

        // An empty landing leaves the chassis and its occupant to take the impact.
        if (!hit)
            _damage.ChangeDamage(entity.Owner, Miss);
    }

    private bool CanTip(EntityUid uid, EntityUid pilot, EntityCoordinates target, out MechComponent mech, out EntityCoordinates landing)
    {
        landing = default;
        mech = default!;
        if (!TryComp<MechComponent>(uid, out var component) || component.Broken || component.Energy < 5 ||
            component.PilotSlot.ContainedEntity != pilot || _gravity.IsWeightless(uid) ||
            Transform(uid).GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !target.IsValid(EntityManager) || Transform(target.EntityId).GridUid != gridUid && target.EntityId != gridUid ||
            !_maps.TryGetTileRef(gridUid, grid, target, out var tile) || _turf.IsSpace(tile) ||
            _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
        {
            return false;
        }

        landing = _maps.GridTileToLocal(gridUid, grid, tile.GridIndices);
        if (_maps.TileIndicesFor(gridUid, grid, Transform(uid).Coordinates) == tile.GridIndices ||
            !_interaction.InRangeUnobstructed(uid, landing, range: TipRange))
        {
            return false;
        }

        mech = component;
        return true;
    }
}
