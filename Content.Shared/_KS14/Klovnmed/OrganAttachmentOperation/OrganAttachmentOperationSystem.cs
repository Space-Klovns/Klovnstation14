using Content.Shared.Body;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Klovnmed.OrganAttachmentOperation;

public sealed class OrganAttachmentOperationSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly BodyHierarchySystem _bodyHierarchySystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    private EntityQuery<OrganComponent> _organQuery;

    /// <summary>
    ///     How much of the damage accumulated is lost every second.
    /// </summary>
    public static readonly TimeSpan ReattachmentDuration = TimeSpan.FromSeconds(3d);

    public override void Initialize()
    {
        base.Initialize();

        _organQuery = GetEntityQuery<OrganComponent>();

        SubscribeLocalEvent<OrganAttachmentOperationComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OrganAttachmentOperationComponent, DoAfterAttemptEvent<OrganAttachmentDoAfterEvent>>(OnDoAfterAttempt);
        SubscribeLocalEvent<OrganAttachmentOperationComponent, OrganAttachmentDoAfterEvent>(OnDoAfter);
    }

    private HashSet<ProtoId<OrganCategoryPrototype>> GetApplicableOrganCategories(Entity<OrganAttachmentOperationComponent> entity)
    {
        var ev = new OrganAttachmentGetCategoriesEvent();
        RaiseLocalEvent(entity, ref ev);

        if (ev.Categories is not { } addedCategories)
            return entity.Comp.BaseOrganCategories;

        foreach (var baseCategory in entity.Comp.BaseOrganCategories)
            addedCategories.Add(baseCategory);

        return addedCategories;
    }

    private void OnInteractUsing(Entity<OrganAttachmentOperationComponent> entity, ref InteractUsingEvent args)
    {
        if (!_organQuery.TryGetComponent(args.Used, out var organComponent) ||
            organComponent.Category is not { } category ||
            !GetApplicableOrganCategories(entity).Contains(category))
            return;

        if (_bodyHierarchySystem.TryGetOrgan(entity.Owner, category, out _) ||
            !_containerSystem.TryGetContainer(entity.Owner, BodyHierarchySystem.ConstContainerId, out var bodyContainer) ||
            !_containerSystem.CanInsert(args.Used, bodyContainer))
        {
            _popupSystem.PopupClient(Loc.GetString("ks-organ-attachment-operation-wontfit"), entity.Owner, args.User);
            return;
        }

        NetEntity? routeEntity = null;
        if (entity.Comp.OrganRoutes.TryGetValue(category, out var routeCategory))
        {
            if (!_bodyHierarchySystem.TryGetOrgan(entity.Owner, routeCategory, out var routeOrganEntity))
            {
                _popupSystem.PopupClient(Loc.GetString("ks-organ-attachment-operation-wontfit"), entity.Owner, args.User);
                return;
            }

            routeEntity = GetNetEntity(routeOrganEntity.Value.Owner);
        }

        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ReattachmentDuration, new OrganAttachmentDoAfterEvent(category, routeEntity ?? GetNetEntity(entity.Owner)), entity.Owner, entity.Owner, used: args.Used)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
            BreakOnDropItem = false,
        });
    }

    private void OnDoAfterAttempt(Entity<OrganAttachmentOperationComponent> entity, ref DoAfterAttemptEvent<OrganAttachmentDoAfterEvent> args)
    {
        if (args.Event is not { } innerEvent ||
            innerEvent.Used is not { } usedUid)
            return;

        if (_bodyHierarchySystem.TryGetOrgan(entity.Owner, args.Event.Category, out _) ||
            !_containerSystem.TryGetContainer(GetEntity(args.Event.ContainerEntity), BodyHierarchySystem.ConstContainerId, out var bodyContainer) ||
            !_containerSystem.CanInsert(usedUid, bodyContainer))
        {
            args.Cancel();
        }
    }

    private void OnDoAfter(Entity<OrganAttachmentOperationComponent> entity, ref OrganAttachmentDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        var containerUid = GetEntity(args.ContainerEntity);
        var container = _containerSystem.GetContainer(containerUid, BodyHierarchySystem.ConstContainerId);
        _containerSystem.Insert(args.Used!.Value, container);

        _popupSystem.PopupClient(
            Loc.GetString("ks-organ-attachment-operation-inserted",
                ("organ", Identity.Name(args.Used.Value, EntityManager, viewer: args.User)),
                ("target", Identity.Name(entity.Owner, EntityManager, viewer: containerUid))
            ), entity.Owner, args.User);
    }
}
