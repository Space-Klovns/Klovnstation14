using System.Linq;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.ZLevel;

public sealed partial class KsZLevelSystem : EntitySystem
{
    private void InitialiseNetworking()
    {
        SubscribeLocalEvent<KsZLevelComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<KsZLevelComponent, ComponentHandleState>(OnHandleState);
    }

    private void OnGetState(Entity<KsZLevelComponent> entity, ref ComponentGetState args)
    {
        args.State = new KsZLevelComponentState(
            [.. entity.Comp.AssociatedStack.Select(x => GetNetEntity(x.Owner))],
            entity.Comp.DepthMultiplier
        );
    }

    // I really don't know why
    private void OnHandleState(Entity<KsZLevelComponent> entity, ref ComponentHandleState args)
    {
        if (args.Current is not KsZLevelComponentState state)
            return;

        entity.Comp.DepthMultiplier = state.DepthMultiplier;

        var newStack = new LinkedList<Entity<KsZLevelComponent>>();
        foreach (var netid in state.AssociatedStack)
        {
            var uid = GetEntity(netid);

            // ERM
            var component = _zLevelQuery.CompOrNull(uid) ?? EnsureComp<KsZLevelComponent>(uid);

            newStack.AddLast((uid, component));
        }

        // So for this entities stack:
        // As the stack is being totally cloned (replicating from server -> client),
        //      and the z-level system relies on AssociatedStack of z-level entities
        //      in the same stack pointing to the same LinkedList<Entity<KsZLevelComponent>>, we will just migrate every
        //      entity's AssociatedStack to point to the new one

        foreach (var migratingEntity in entity.Comp.AssociatedStack)
        {
            migratingEntity.Comp.AssociatedStack = newStack;

            // look, im lazy OK?
            migratingEntity.Comp.Node = newStack.Find(migratingEntity)!;
            DebugTools.AssertNotNull(migratingEntity.Comp.Node);
        }
    }
}
