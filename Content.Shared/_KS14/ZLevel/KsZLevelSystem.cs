using System.Runtime.CompilerServices;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.ZLevel;

/*
    Read this before contributing to this undocumented code!
    Here are some definitions about z-levels:
    - A z-level is a map entity.
    - A z-level stack is a linkedlist.

    - Each z-level is part of a stack, even if its the only member.
    - A z-level can only be part of one stack at once; no two stacks can have the same z-level.

    IMPORTANT:
    - Every z-level entity has a AssociatedStack, pointing to a LinkedList<Entity<KsZLevelComponent>>
    - Every z-level entity in the same stack will point to the same internal LinkedList<Entity<KsZLevelComponent>> object
        So:
        two z-levels that seem like theyre in the same stack, with AssociatedStacks that share the exact
            same values, but point to different LinkedList<Entity<KsZLevelComponent>>s, are NOT in the same stack and this should not happen!
*/

public sealed partial class KsZLevelSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;

    /// <summary>
    ///     <see cref="KsCCVars.ZLevelParallaxStrength"/>, clamped to what <see cref="GetDepthScale"/> can
    ///         actually use.
    /// </summary>
    private float _parallaxStrength = 1f;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(
            _configurationManager,
            KsCCVars.ZLevelParallaxStrength,
            value => _parallaxStrength = MathF.Max(value, 0f),
            true
        );

        InitialiseNetworking();
    }

    [SubscribeLocalEvent]
    private void OnInit(Entity<KsZLevelComponent> entity, ref ComponentInit args)
    {
        // SetDepth clamps, but a Depth written straight into the DataField never passes through it - and the
        //      render passes multiply and accumulate it without clamping, so a zero flattens the stack and a
        //      negative inverts it. Done here so there is nowhere a bad one can enter from.
        entity.Comp.Depth = MathF.Max(entity.Comp.Depth, MinimumDepth);

        // No data
        if (entity.Comp.AssociatedStack.Count == 0)
        {
            entity.Comp.AssociatedStack.AddFirst(entity);
            entity.Comp.Node = entity.Comp.AssociatedStack.First!;
            return;
        }

        // Inited with data
        DebugTools.Assert(entity.Comp.AssociatedStack.Contains(entity), "Upon initialising with a non-empty stack, z-level entity was not in its own stack");
        entity.Comp.Node = entity.Comp.AssociatedStack.Find(entity)!;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsZLevelComponent> entity, ref ComponentShutdown args)
    {
        var departedStack = entity.Comp.AssociatedStack;
        RemoveFromOwnStack(entity);

        // Every z-level replicates the whole stack, so the survivors all have a stale state now.
        foreach (var survivingEntity in departedStack)
            Dirty(survivingEntity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromOwnStack(Entity<KsZLevelComponent> entity)
    {
        DebugTools.Assert(
            entity.Comp.AssociatedStack.Contains(entity),
            $"While trying to remove it from its own stack, realised that Z-Level {ToPrettyString(entity.Owner)}'s stack does not contain it!"
        );

        if (entity.Comp.AssociatedStack.Count == 1)
            entity.Comp.AssociatedStack.Clear();
        else
            entity.Comp.AssociatedStack.Remove(entity);
    }

    /// <summary>
    ///     Tries fill the provided list with the z-level entities below this z-level in ascending order;
    ///         the bottom-most valid z-level will be added to the list first, and top-most one will be added last.
    /// </summary>
    /// <param name="entitiesBelow">List to operate on.</param>
    /// <returns>
    ///     True if <paramref name="entity"/> is a z-level at all, which is not the same as there being anything
    ///         under it - the bottom of a stack answers true and adds nothing. Check the list, not this, for
    ///         whether there is anything below.
    /// </returns>
    public bool TryGetZLevelsBelow(Entity<KsZLevelComponent?> entity, List<Entity<KsZLevelComponent>> entitiesBelow)
    {
        // Everything under a gap is everything under its anchor, plus the anchor itself - a gap sits above
        //      that floor plane, so the floor plane is one of the things below it.
        if (TryResolveGapAnchor(entity.Owner, out var anchorEntity, out _))
        {
            if (!TryGetZLevelsBelow(anchorEntity.Value!, entitiesBelow))
                return false;

            entitiesBelow.Add(anchorEntity.Value);
            return true;
        }

        if (!_zLevelQuery.Resolve(ref entity, logMissing: false))
            return false;

        // ascending
        for (var node = entity.Comp!.AssociatedStack.First; node != null && node.Value != entity!; node = node.Next)
            entitiesBelow.Add(node.Value);

        return true;
    }
}
