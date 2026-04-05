using Content.Shared.Construction;
using Robust.Shared.Prototypes;
using Content.Shared._KS14.Klovnmed;
using Content.Shared.Body;
using Robust.Server.GameObjects;
using Content.Server.Hands.Systems;

namespace Content.Server._KS14.Construction.Completions;

[DataDefinition]
public sealed partial class TakeOrgan : IGraphAction
{
    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly BodyHierarchySystem _bodyHierarchySystem = default!;

    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> Category = "";

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        if (!_bodyHierarchySystem.TryGetOrgan(uid, Category, out var organUid))
            return;

        _transformSystem.SetCoordinates(organUid.Value, entityManager.GetComponent<TransformComponent>(uid).Coordinates);
        if (userUid is { })
            _handsSystem.TryPickupAnyHand(userUid.Value, organUid.Value, animateUser: true);
    }
}
