using System.Threading;
using System.Threading.Tasks;
using Content.Shared._Goobstation.Silicon.Bots;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Emag.Components;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using System.Linq;
using Content.Shared.Repairable;

namespace Content.Server._Goobstation.NPC.HTN.PrimitiveTasks.Operators.Specific;

public sealed partial class PickNearbyWeldableOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private EntityLookupSystem _lookup = default!;
    private WeldbotSystem _weldbot = default!;
    private PathfindingSystem _pathfinding = default!;
    private DamageableSystem _damageableSystem = default!;
    private TagSystem _tagSystem = default!;

    [DataField]
    public string RangeKey = NPCBlackboard.WeldbotWeldRange;

    /// <summary>
    /// Target entity to weld
    /// </summary>
    [DataField(required: true)]
    public string TargetKey = string.Empty;

    /// <summary>
    /// Target entitycoordinates to move to.
    /// </summary>
    [DataField(required: true)]
    public string TargetMoveKey = string.Empty;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _lookup = sysManager.GetEntitySystem<EntityLookupSystem>();
        _weldbot = sysManager.GetEntitySystem<WeldbotSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
        _damageableSystem = sysManager.GetEntitySystem<DamageableSystem>();
        _tagSystem = sysManager.GetEntitySystem<TagSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<float>(RangeKey, out var range, _entManager) || !_entManager.TryGetComponent<WeldbotComponent>(owner, out var weldbot))
            return (false, null);

        var damageQuery = _entManager.GetEntityQuery<DamageableComponent>();
        var emagged = _entManager.HasComponent<EmaggedComponent>(owner);
        var weldbotDamageGroups = _weldbot.GetDamageAmountGroups(weldbot, _prototypeManager);

        foreach (var target in _lookup.GetEntitiesInRange(owner, range))
        {
            if (!damageQuery.TryGetComponent(target, out var damage))
                continue;

            if (!_entManager.TryGetComponent<RepairableComponent>(target, out var repairComp))
                continue;

            var targetDamage = _damageableSystem.GetDamagePerGroup((target, damage));
            if (!emagged && !targetDamage.Keys.Intersect(weldbotDamageGroups.Keys).Any(key => targetDamage.TryGetValue(key, out var value) ? value > 0 : false))
                continue;

            //Needed to make sure it doesn't sometimes stop right outside it's interaction range
            var pathRange = SharedInteractionSystem.InteractionRange - 0.5f;
            var path = await _pathfinding.GetPath(owner, target, pathRange, cancelToken);

            if (path.Result == PathResult.NoPath)
                continue;

            return (true, new Dictionary<string, object>()
            {
                {TargetKey, target},
                {TargetMoveKey, _entManager.GetComponent<TransformComponent>(target).Coordinates},
                {NPCBlackboard.PathfindKey, path},
            });
        }

        return (false, null);
    }
}
