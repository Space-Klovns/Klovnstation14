using Content.Shared.Construction;
using Robust.Shared.Prototypes;
using Content.Shared._KS14.Klovnmed;
using Content.Shared.Body;
using Content.Server.Hands.Systems;
using Robust.Server.Containers;
using Content.Shared._KS14.Deferral;

namespace Content.Server._KS14.Construction.Completions;

[DataDefinition]
public sealed partial class TakeOrgan : IGraphAction
{
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly BodyHierarchySystem _bodyHierarchySystem = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;

    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> Category = "";

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        if (!_bodyHierarchySystem.TryGetOrgan(uid, Category, out var organEntity) ||
            !_containerSystem.TryGetContainingContainer(organEntity.Value.Owner, out var organContainer))
            return;

        _containerSystem.Remove(organEntity.Value.Owner, organContainer, force: true, destination: entityManager.GetComponent<TransformComponent>(uid).Coordinates);

        // This needs to be deferred to next tick because lol the organ isnt removed from body on client and thus cant be inserted into hand
        // yet otherwise 220 things break :joy: thank you rt
        if (userUid is { })
            SynchronousDeferralSystem.Defer(() =>
            {
                _handsSystem.TryPickupAnyHand(userUid.Value, organEntity.Value, animateUser: true);
            });
    }
}
