using System.Linq;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel;

public sealed partial class KsZLevelSystem : EntitySystem
{
    private void InitialiseNetworking()
    {
    }

    [SubscribeLocalEvent]
    private void OnGetState(Entity<KsZLevelComponent> entity, ref ComponentGetState args)
    {
        args.State = new KsZLevelComponentState(
            [.. entity.Comp.AssociatedStack.Select(x => GetNetEntity(x.Owner))],
            entity.Comp.Depth
        );
    }

    /*
        The whole stack is replicated on every z-level in it, so any one of these states is enough to rebuild
            it. What the rebuild has to preserve is the system's core invariant: every z-level in a stack points
            at the *same* LinkedList object. Two stacks that look identical but are different objects are not
            the same stack, and everything downstream that walks Node.Previous/Node.Next quietly breaks.

        So this does not patch the existing list, it builds the new one and then repoints every member at it -
            including members that were in the old stack and have now dropped out of it, which get handed a
            fresh single-member stack of their own rather than being left pointing at a list they are not in.
    */
    [SubscribeLocalEvent]
    private void OnHandleState(Entity<KsZLevelComponent> entity, ref ComponentHandleState args)
    {
        if (args.Current is not KsZLevelComponentState state)
            return;

        entity.Comp.Depth = state.Depth;

        var newStack = new LinkedList<Entity<KsZLevelComponent>>();
        foreach (var netEntity in state.AssociatedStack)
        {
            var uid = GetEntity(netEntity);
            if (!Exists(uid))
            {
                // Map entities are always replicated, so this means the state itself is malformed. Dropping the
                //      entry would silently reorder the stack, which is worse than an incomplete one.
                Log.Error($"Z-level stack replicated to {ToPrettyString(entity.Owner)} referenced unknown entity {netEntity}; the stack will be incomplete.");
                continue;
            }

            newStack.AddLast((uid, _zLevelQuery.CompOrNull(uid) ?? EnsureComp<KsZLevelComponent>(uid)));
        }

        // Anything that left this stack gets its own, so it is never a member of a list it does not point at.
        foreach (var departingEntity in entity.Comp.AssociatedStack)
        {
            if (newStack.Contains(departingEntity))
                continue;

            var departingStack = new LinkedList<Entity<KsZLevelComponent>>();
            departingEntity.Comp.AssociatedStack = departingStack;
            departingEntity.Comp.Node = departingStack.AddFirst(departingEntity);
        }

        for (var node = newStack.First; node != null; node = node.Next)
        {
            var memberEntity = node.Value;
            memberEntity.Comp.AssociatedStack = newStack;
            memberEntity.Comp.Node = node;
        }
    }
}
