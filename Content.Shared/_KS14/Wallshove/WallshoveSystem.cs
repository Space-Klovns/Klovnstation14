using Robust.Shared.Physics.Systems;

namespace Content.Shared._KS14.Wallshove;

public sealed class WallshoveSystem : EntitySystem
{
    [Dependency] private readonly RayCastSystem _rayCastSystem = default!;
}
