using Content.Shared._KS14.EventNetworking;
using Content.Shared._KS14.Hierarchy;
using Content.Shared.Body;
using Robust.Shared.Containers;
using Robust.Shared.Player;

namespace Content.Shared._KS14.Klovnmed;

public sealed class BodyHierarchySystem : BaseHierarchySystem<BodyComponent, OrganComponent>
{
    [Dependency] private readonly EventNetworkingSystem _eventNetworkingSystem = default!;

    public const string ConstContainerId = "body_organs"; // for compatibility

    public override string ContainerId => ConstContainerId;
    public override bool ServerOnly => true;

    public override void Initialize()
    {
        base.Initialize();

        // if (NetManager.IsClient)
        // {
        //     _eventNetworkingSystem.SubscribeNetworkedLocalEvent<BodyComponent, NetOrganInsertedIntoEvent>(OnNetOIIE);
        //     _eventNetworkingSystem.SubscribeNetworkedLocalEvent<OrganComponent, NetOrganGotInsertedEvent>(OnNetOGIE);

        //     _eventNetworkingSystem.SubscribeNetworkedLocalEvent<BodyComponent, NetOrganRemovedFromEvent>(OnNetORFE);
        //     _eventNetworkingSystem.SubscribeNetworkedLocalEvent<OrganComponent, NetOrganGotRemovedEvent>(OnNetOGRE);
        // }

        SubscribeLocalEvent<OrganComponent, ContainerIsRemovingAttemptEvent>(OnOrganElementRemovingAttempt);
    }

    private void OnNetOIIE(Entity<BodyComponent> entity, ref NetOrganInsertedIntoEvent args)
    {
        if (!TryGetEntity(args.Organ, out var uid))
            return;

        var ev = new OrganInsertedIntoEvent(uid.Value);
        RaiseLocalEvent(entity, ref ev);
    }

    private void OnNetOGIE(Entity<OrganComponent> entity, ref NetOrganGotInsertedEvent args)
    {
        if (!TryGetEntity(args.Target, out var uid))
            return;

        var ev = new OrganGotInsertedEvent(uid.Value);
        RaiseLocalEvent(entity, ref ev);
    }

    private void OnNetORFE(Entity<BodyComponent> entity, ref NetOrganRemovedFromEvent args)
    {
        if (!TryGetEntity(args.Organ, out var uid))
            return;

        var ev = new OrganRemovedFromEvent(uid.Value);
        RaiseLocalEvent(entity, ref ev);
    }

    private void OnNetOGRE(Entity<OrganComponent> entity, ref NetOrganGotRemovedEvent args)
    {
        if (!TryGetEntity(args.Target, out var uid))
            return;

        var ev = new OrganGotRemovedEvent(uid.Value);
        RaiseLocalEvent(entity, ref ev);
    }

    private void OnOrganElementRemovingAttempt(Entity<OrganComponent> entity, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID != ContainerId)
            return;

        // I just want contents to be visible not removable. Still interactable and whatnot doe
        args.Cancel();
    }

    protected override void AddElementToHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> addedEntity)
    {
        base.AddElementToHierarchy(hierarchyEntity, addedEntity);


        var body = new OrganInsertedIntoEvent(addedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotInsertedEvent(hierarchyEntity);
        RaiseLocalEvent(addedEntity, ref ev);

        // var pvsFilter = Filter.Pvs(hierarchyEntity);
        // var netBody = new NetOrganInsertedIntoEvent(GetNetEntity(addedEntity.Owner));
        // _eventNetworkingSystem.NetworkLocalEvent(hierarchyEntity, pvsFilter, netBody);
        // var netEv = new NetOrganGotInsertedEvent(GetNetEntity(hierarchyEntity.Owner));
        // _eventNetworkingSystem.NetworkLocalEvent(addedEntity, pvsFilter, netEv);

        addedEntity.Comp.Container.ShowContents = true;
    }

    protected override void RemoveElementFromHierarchy(Entity<BodyComponent> hierarchyEntity, Entity<OrganComponent> removedEntity)
    {
        base.RemoveElementFromHierarchy(hierarchyEntity, removedEntity);

        var body = new OrganRemovedFromEvent(removedEntity);
        RaiseLocalEvent(hierarchyEntity, ref body);

        var ev = new OrganGotRemovedEvent(hierarchyEntity);
        RaiseLocalEvent(removedEntity, ref ev);

        // var pvsFilter = Filter.Pvs(hierarchyEntity);
        // var netBody = new NetOrganRemovedFromEvent(GetNetEntity(removedEntity.Owner));
        // _eventNetworkingSystem.NetworkLocalEvent(hierarchyEntity, pvsFilter, netBody);
        // var netEv = new NetOrganGotRemovedEvent(GetNetEntity(hierarchyEntity.Owner));
        // _eventNetworkingSystem.NetworkLocalEvent(removedEntity, pvsFilter, netEv);

        removedEntity.Comp.Container.ShowContents = false;
    }

    protected override void UpdateHierarchyEntityState(Entity<BodyComponent> entity)
    {
        Dirty(entity);
    }

    protected override void UpdateElementEntityChildren(Entity<OrganComponent> entity)
    {
        Dirty(entity);
    }

    protected override void UpdateElementEntityHierarchy(Entity<OrganComponent> entity)
    {
        Dirty(entity);
    }
}

[ByRefEvent]
public readonly record struct NetOrganGotInsertedEvent(NetEntity Target);

[ByRefEvent]
public readonly record struct NetOrganGotRemovedEvent(NetEntity Target);

[ByRefEvent]
public readonly record struct NetOrganInsertedIntoEvent(NetEntity Organ);

[ByRefEvent]
public readonly record struct NetOrganRemovedFromEvent(NetEntity Organ);
