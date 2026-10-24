using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private EntityQuery<FixturesComponent> _ksFixturesQuery = default!;

    /// <summary>
    ///     Whether a weightless mover can push off the other entity: whether any of their hard fixtures would collide.
    ///         A body's collision layer and mask are the union of all its fixtures, sensors included, so comparing
    ///         those lets a mover push off something it passes straight through, such as a puddle.
    /// </summary>
    /// <param name="moverLayer">The mover's hard fixtures' layers, from <see cref="GetHardCollisionBits"/>.</param>
    /// <param name="moverMask">The mover's hard fixtures' masks, from <see cref="GetHardCollisionBits"/>.</param>
    private bool KsCanPushOff(int moverLayer, int moverMask, EntityUid otherUid)
    {
        if (moverLayer == 0 && moverMask == 0)
            return false;

        if (!_ksFixturesQuery.TryComp(otherUid, out var otherFixturesComponent))
            return false;

        foreach (var otherFixture in otherFixturesComponent.Fixtures.Values)
        {
            if (!otherFixture.Hard)
                continue;

            if ((moverMask & otherFixture.CollisionLayer) != 0 || (otherFixture.CollisionMask & moverLayer) != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The union of the layers and masks of an entity's hard fixtures only.
    /// </summary>
    private void GetHardCollisionBits(EntityUid uid, out int layer, out int mask)
    {
        layer = 0;
        mask = 0;

        if (!_ksFixturesQuery.TryComp(uid, out var fixturesComponent))
            return;

        foreach (var fixture in fixturesComponent.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            layer |= fixture.CollisionLayer;
            mask |= fixture.CollisionMask;
        }
    }
}
