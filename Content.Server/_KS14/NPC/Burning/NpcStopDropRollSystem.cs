using Content.Server._KS14.NPC.Dangers;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos.Components;
using Content.Shared.NPC;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Burning;

/// <summary>
///     Has a burning NPC stop, drop and roll, as a player would with the fire alert. It goes for every awake NPC that can,
///         whatever its root, so this is a reflex here and not a branch in each of them. Not on a fire, which would only
///         light it again, nor beside one it is on its way from. See <see cref="NpcStopDropRollComponent"/>.
/// </summary>
public sealed partial class NpcStopDropRollSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private FlammableSystem _flammableSystem = default!;
    [Dependency] private NPCSteeringSystem _npcSteeringSystem = default!;
    [Dependency] private PathfindingSystem _pathfindingSystem = default!;

    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;
    [Dependency] private EntityQuery<NpcStopDropRollComponent> _stopDropRollQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private float _minFireStacks;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.NpcFireRollMinStacks, value => _minFireStacks = value, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!NpcThrottle.TryConsume(ref _nextUpdate, _gameTiming.CurTime, UpdateInterval))
            return;

        var enumerator = EntityQueryEnumerator<ActiveNPCComponent, FlammableComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out _, out var flammableComponent, out var transformComponent))
        {
            if (!flammableComponent.OnFire ||
                !flammableComponent.CanResistFire ||
                flammableComponent.Resisting ||
                flammableComponent.FireStacks < _minFireStacks)
                continue;

            _stopDropRollQuery.TryComp(uid, out var stopDropRollComponent);

            if (IsBusy(uid, stopDropRollComponent) || !IsSomewhereToRoll(uid, transformComponent))
                continue;

            _flammableSystem.Resist(uid, flammableComponent, keepHold: stopDropRollComponent?.KeepsHold ?? false);
        }
    }

    private bool IsBusy(EntityUid uid, NpcStopDropRollComponent? stopDropRollComponent)
    {
        return stopDropRollComponent?.BusyKey is { } busyKey &&
            _htnQuery.TryComp(uid, out var htnComponent) &&
            htnComponent.Blackboard.ContainsKey(busyKey, EntityManager);
    }

    /// <summary>
    ///     Fire whether or not it is something <paramref name="uid"/> avoids: one that shrugs off a fire on the floor still
    ///         gets nowhere rolling about in it.
    /// </summary>
    private bool IsSomewhereToRoll(EntityUid uid, TransformComponent transformComponent)
    {
        var (posed, near) = _pathfindingSystem.GetDangersAt(transformComponent.Coordinates);

        if ((posed & NpcTileDangerKind.Fire) != 0x0)
            return false;

        return (near & NpcTileDangerKind.Fire) == 0x0 || !_npcSteeringSystem.IsFollowingPath(uid);
    }
}
