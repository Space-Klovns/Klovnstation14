using System.Runtime.CompilerServices;
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
    [Dependency] private readonly EntityQuery<KsZLevelComponent> _zLevelQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsZLevelComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<KsZLevelComponent, ComponentShutdown>(OnShutdown);

        InitialiseNetworking();
    }

    private void OnInit(Entity<KsZLevelComponent> entity, ref ComponentInit args)
    {
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

    private void OnShutdown(Entity<KsZLevelComponent> entity, ref ComponentShutdown args)
    {
        DebugTools.Assert(
            entity.Comp.AssociatedStack.Contains(entity),
            $"While trying to remove it from its own stack, realised that Z-Level {ToPrettyString(entity.Owner)}'s stack does not contain it!"
        );

        if (entity.Comp.AssociatedStack.Count != 1)
            entity.Comp.AssociatedStack.Remove(entity);
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
    /// <returns>True if anything was added to <paramref name="entitiesBelow"/>.</returns>
    public bool TryGetZLevelsBelow(Entity<KsZLevelComponent?> entity, List<Entity<KsZLevelComponent>> entitiesBelow)
    {
        if (!_zLevelQuery.Resolve(ref entity, logMissing: false))
            return false;

        // ascending
        for (var node = entity.Comp!.AssociatedStack.First; node != null && node.Value != entity!; node = node.Next)
            entitiesBelow.Add(node.Value);

        return true;
    }
}
