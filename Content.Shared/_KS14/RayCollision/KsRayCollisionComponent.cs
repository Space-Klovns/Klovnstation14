using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Physics.Dynamics;

namespace Content.Shared._KS14.RayCollision;

[RegisterComponent, NetworkedComponent]
[Access(typeof(KsRayCollisionSystem))]
public sealed partial class KsRayCollisionComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public MapCoordinates LastMapCoordinates;

    /// <summary>
    ///     If not-null, only these fixtures will be considered for collision.
    ///         Otherwise, all fixtures on the entity will be considered for collision.
    ///
    ///     Non-hard fixtures do not apply. This list is of the aforementioned fixtures' IDs.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public string[]? ExclusivelyCheckedFixtures = [];
}

[ByRefEvent]
public record struct KsRayCollisionEvent(Entity<TransformComponent> OurEntity, Entity<TransformComponent> OtherEntity, EntityCoordinates Point, string OurFixtureId, Fixture OurFixture);
