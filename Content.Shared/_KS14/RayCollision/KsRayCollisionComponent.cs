using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._KS14.RayCollision;

[RegisterComponent, NetworkedComponent]
[Access(typeof(KsRayCollisionSystem))]
public sealed partial class KsRayCollisionComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public MapCoordinates LastMapCoordinates;
}

[ByRefEvent]
public record struct KsRayCollisionEvent(Entity<TransformComponent> OurEntity, Entity<TransformComponent> OtherEntity, EntityCoordinates Point);
