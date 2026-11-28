using System.Numerics;
using Content.Server._KS14.Cultivation.Systems;
using Content.Server.Construction.Components;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Stack;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Triggers;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Cultivation.Body;

/// <summary>
///     Breaks things the way a body art should: into rubble and scrap, never into nothing. A wall comes down to its
///         girder with half its plating strewn about, and only a devastating blow takes the girder too.
/// </summary>
public sealed partial class CultivationRubbleSystem : EntitySystem
{
    private static readonly EntProtoId RubbleEffect = "KsFxRubble";
    private static readonly EntProtoId DustEffect = "KsBodyDust";
    private static readonly SoundSpecifier WallFallSound = new SoundCollectionSpecifier("MetalSlam");

    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StackSystem _stackSystem = default!;

    /// <summary>
    ///     How much of the damage wanted actually lands. Overkill makes most structures vanish without leaving anything,
    ///         so a blow that would go past the last threshold stops just short of it, at the one that leaves debris.
    /// </summary>
    public float Capped(EntityUid uid, float desired)
    {
        if (!TryComp<DestructibleComponent>(uid, out var destructibleComponent) || !HasComp<DamageableComponent>(uid))
            return desired;

        var highest = FixedPoint2.Zero;
        var count = 0;
        foreach (var threshold in destructibleComponent.Thresholds)
        {
            if (threshold.Trigger is not DamageTrigger damageTrigger)
                continue;

            count++;
            if (damageTrigger.Damage > highest)
                highest = damageTrigger.Damage;
        }

        if (count < 2)
            return desired;

        var room = (float)(highest - _damageableSystem.GetTotalDamage(uid)) - 1f;
        return MathF.Max(0f, MathF.Min(desired, room));
    }

    /// <summary>
    ///     Built to take far more than a plain wall: its last threshold is past a plain wall's 400.
    /// </summary>
    public bool IsReinforced(EntityUid uid)
    {
        if (!TryComp<DestructibleComponent>(uid, out var destructibleComponent))
            return false;

        foreach (var threshold in destructibleComponent.Thresholds)
        {
            if (threshold.Trigger is DamageTrigger damageTrigger && damageTrigger.Damage > 400)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Brings a wall down. It falls to its girder with half its plating scattered round it; a devastating blow
    ///         takes the girder too and leaves only scrap.
    /// </summary>
    public void BreakWall(EntityUid wallUid, bool devastate, EntityUid? userUid)
    {
        if (TerminatingOrDeleted(wallUid) || EntityManager.IsQueuedForDeletion(wallUid))
            return;

        var coordinates = Transform(wallUid).Coordinates;
        var girderNode = GirderNode(wallUid);
        Rubble(_transformSystem.ToMapCoordinates(coordinates));

        if (!devastate && girderNode != null && TryComp<DestructibleComponent>(wallUid, out var destructibleComponent))
        {
            // Drop the plating first: the girder that replaces the wall forgets what was on it.
            ScatterMaterials(wallUid, coordinates, girderNode);
            if (GirderThreshold(destructibleComponent) is { } girderDamage && HasComp<DamageableComponent>(wallUid))
            {
                var needed = (float)(girderDamage - _damageableSystem.GetTotalDamage(wallUid)) + 1f;
                if (needed > 0f)
                    _damageableSystem.TryChangeDamage(wallUid, CultivationSystem.Damage("Structural", needed), ignoreResistances: true, origin: userUid);
            }

            return;
        }

        ScatterMaterials(wallUid, coordinates, fromNode: null);
        _audioSystem.PlayPvs(WallFallSound, coordinates);
        QueueDel(wallUid);
    }

    /// <summary>
    ///     A puff of dust and flying chips where something gave way.
    /// </summary>
    public void Rubble(MapCoordinates coordinates)
    {
        Spawn(RubbleEffect, coordinates);
        Spawn(DustEffect, coordinates);
    }

    private static FixedPoint2? GirderThreshold(DestructibleComponent destructibleComponent)
    {
        foreach (var threshold in destructibleComponent.Thresholds)
        {
            if (threshold.Trigger is not DamageTrigger damageTrigger)
                continue;

            foreach (var behavior in threshold.Behaviors)
            {
                if (behavior is ChangeConstructionNodeBehavior)
                    return damageTrigger.Damage;
            }
        }

        return null;
    }

    private string? GirderNode(EntityUid wallUid)
    {
        if (!TryComp<DestructibleComponent>(wallUid, out var destructibleComponent))
            return null;

        foreach (var threshold in destructibleComponent.Thresholds)
        {
            foreach (var behavior in threshold.Behaviors)
            {
                if (behavior is ChangeConstructionNodeBehavior changeNodeBehavior && !string.IsNullOrEmpty(changeNodeBehavior.Node))
                    return changeNodeBehavior.Node;
            }
        }

        return null;
    }

    /// <summary>
    ///     Half of what went into the wall, from <paramref name="fromNode"/> (or from scratch) up to what it is now,
    ///         rounded up so even a single sheet's worth leaves something behind.
    /// </summary>
    private void ScatterMaterials(EntityUid wallUid, EntityCoordinates coordinates, string? fromNode)
    {
        if (!TryComp<ConstructionComponent>(wallUid, out var constructionComponent)
            || !_prototypeManager.TryIndex(constructionComponent.Graph, out ConstructionGraphPrototype? graphPrototype))
            return;

        var startNode = fromNode ?? graphPrototype.Start;
        if (startNode == null || !graphPrototype.TryPath(startNode, constructionComponent.Node, out var path))
            return;

        var materials = new Dictionary<ProtoId<StackPrototype>, int>();
        var previous = startNode;
        foreach (var node in path)
        {
            if (node.Name == previous)
                continue;

            if (graphPrototype.Edge(previous, node.Name) is { } edge)
            {
                foreach (var step in edge.Steps)
                {
                    if (step is MaterialConstructionGraphStep materialStep)
                        materials[materialStep.MaterialPrototypeId] = materials.GetValueOrDefault(materialStep.MaterialPrototypeId) + materialStep.Amount;
                }
            }

            previous = node.Name;
        }

        foreach (var (material, amount) in materials)
        {
            var dropped = (amount + 1) / 2;
            for (var i = 0; i < dropped; i++)
            {
                var offset = new Vector2(_robustRandom.NextFloat(-0.4f, 0.4f), _robustRandom.NextFloat(-0.4f, 0.4f));
                _stackSystem.SpawnAtPosition(1, material, coordinates.Offset(offset));
            }
        }
    }
}
