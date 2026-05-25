using Content.Shared.DragDrop;
using Robust.Shared.Containers;

namespace Content.Shared.Body;

/// <summary>
/// System responsible for coordinating entities with <see cref="BodyComponent" /> and their entities with <see cref="OrganComponent" />.
/// This system is primarily responsible for event relaying and the relationships between a body and its organs.
/// It is not responsible for player-facing body features, e.g. "blood" or "breathing."
/// Such features should be implemented in systems relying on the various events raised by this class.
/// </summary>
/// <seealso cref="OrganGotInsertedEvent" />
/// <seealso cref="OrganGotRemovedEvent" />
/// <seealso cref="OrganInsertedIntoEvent" />
/// <seealso cref="OrganRemovedFromEvent" />
/// <seealso cref="BodyRelayedEvent{TEvent}" />
public sealed partial class BodySystem : EntitySystem
{
    // KS14: Unused system, removed

    [Dependency] private readonly EntityQuery<BodyComponent> _bodyQuery = default!;
    [Dependency] private readonly EntityQuery<OrganComponent> _organQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        // SubscribeLocalEvent<BodyComponent, ComponentInit>(OnBodyInit); // KS14: Commented in favour of hierarchy system
        // SubscribeLocalEvent<BodyComponent, ComponentShutdown>(OnBodyShutdown); // KS14: Commented in favour of hierarchy system

        SubscribeLocalEvent<BodyComponent, CanDragEvent>(OnCanDrag);

        // SubscribeLocalEvent<BodyComponent, EntInsertedIntoContainerMessage>(OnBodyEntInserted); // KS14: Commented in favour of hierarchy system
        // SubscribeLocalEvent<BodyComponent, EntRemovedFromContainerMessage>(OnBodyEntRemoved); // KS14: Commented in favour of hierarchy system

        InitializeRelay();
        InitializeKlovn(); // KS14
    }

    // KS14: Commented in favour of hierarchy system
    // private void OnBodyInit(Entity<BodyComponent> ent, ref ComponentInit args)
    // {
    //     ent.Comp.Organs =
    //         _container.EnsureContainer<Container>(ent, BodyComponent.ContainerID);
    // }

    // KS14: Commented in favour of hierarchy system
    // private void OnBodyShutdown(Entity<BodyComponent> ent, ref ComponentShutdown args)
    // {
    //     if (ent.Comp.Organs is { } organs)
    //         _container.ShutdownContainer(organs);
    // }

    // KS14: Commented in favour of hierarchy system
    // private void OnBodyEntInserted(Entity<BodyComponent> ent, ref EntInsertedIntoContainerMessage args)
    // {
    //     if (args.Container.ID != BodyComponent.ContainerID)
    //         return;

    //     if (!_organQuery.TryComp(args.Entity, out var organ))
    //         return;

    //     var body = new OrganInsertedIntoEvent(args.Entity);
    //     RaiseLocalEvent(ent, ref body);

    //     var ev = new OrganGotInsertedEvent(ent);
    //     RaiseLocalEvent(args.Entity, ref ev);

    //     if (organ.Body != ent)
    //     {
    //         organ.Body = ent;
    //         Dirty(args.Entity, organ);
    //     }
    // }

    // KS14: Commented in favour of hierarchy system
    // private void OnBodyEntRemoved(Entity<BodyComponent> ent, ref EntRemovedFromContainerMessage args)
    // {
    //     if (args.Container.ID != BodyComponent.ContainerID)
    //         return;

    //     if (!_organQuery.TryComp(args.Entity, out var organ))
    //         return;

    //     var body = new OrganRemovedFromEvent(args.Entity);
    //     RaiseLocalEvent(ent, ref body);

    //     var ev = new OrganGotRemovedEvent(ent);
    //     RaiseLocalEvent(args.Entity, ref ev);

    //     if (organ.Body == null)
    //         return;

    //     organ.Body = null;
    //     Dirty(args.Entity, organ);
    // }

    private void OnCanDrag(Entity<BodyComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }
}
