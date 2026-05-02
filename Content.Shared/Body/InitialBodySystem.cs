using System.Numerics;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Shared.Body;

public sealed class InitialBodySystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InitialBodyComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<InitialBodyComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<ContainerManagerComponent>(ent, out var containerComp))
            return;

        if (TerminatingOrDeleted(ent) || !Exists(ent))
            return;

        // KS14: Use hierarchy instead of container

        // KS14 Start
        void Recurse(InitialBodyPart initialBodyPart, EntityUid parent)
        {
            ent.Comp.TotalCategories.Add(initialBodyPart.Category);

            var spawnedUid = Spawn(initialBodyPart.Entity);
            _container.Insert(spawnedUid, _container.GetContainer(parent, _KS14.Klovnmed.BodyHierarchySystem.ConstContainerId));

            if (initialBodyPart.Children is not { } children)
                return;

            foreach (var arrangement in children)
                Recurse(arrangement, spawnedUid);
        }

        foreach (var arrangement in ent.Comp.Organs)
            Recurse(arrangement, ent);

        Dirty(ent); // KS14
        // KS14 End

        // KS14: Slopcode commented out
        // foreach (var proto in ent.Comp.Organs.Values)
        // {
        //     // TODO: When e#6192 is merged replace this all with TrySpawnInContainer...
        //     var spawn = Spawn(proto, coords);

        //     if (!_container.Insert(spawn, container, containerXform: xform))
        //     {
        //         Log.Error($"Entity {ToPrettyString(ent)} with a {nameof(InitialBodyComponent)} failed to insert an entity: {ToPrettyString(spawn)}.\n");
        //         Del(spawn);
        //     }
        // }
    }
}
