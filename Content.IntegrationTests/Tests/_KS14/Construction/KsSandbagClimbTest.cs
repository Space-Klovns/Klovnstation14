#nullable enable
using Content.IntegrationTests.Tests.Movement;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Construction;

/// <summary>
///     A sandbag wall stops anyone walking into it, but can be climbed over like a table.
/// </summary>
[TestFixture]
[TestOf(typeof(ClimbSystem))]
public sealed class KsSandbagClimbTest : MovementTest
{
    private static readonly EntProtoId SandbagWall = "KsSandbagWall";

    [Test]
    public async Task SandbagWallCanBeClimbedOver()
    {
        // A wall to the player's east.
        await SpawnTarget(SandbagWall);
        Assert.That(Delta(), Is.GreaterThan(0));

        // Walking into it gets nowhere.
        await Move(DirectionFlag.East, 1f);
        Assert.That(Delta(), Is.GreaterThan(0), "a sandbag wall should stop someone walking into it");
        await Server.WaitAssertion(() => Assert.That(
            SEntMan.System<SharedPhysicsSystem>().IsHardCollidable(ToServer(Player), ToServer(Target!.Value)),
            "someone walking should collide with a sandbag wall"));

        var climbingComponent = Comp<ClimbingComponent>(Player);
        await Server.WaitPost(() => SEntMan.System<ClimbSystem>().TryClimb(ToServer(Player), ToServer(Player), ToServer(Target!.Value), out _));
        await AwaitDoAfters();
        Assert.That(climbingComponent.IsClimbing, "the player should be climbing the sandbags");

        // Physics doesn't push a body out of a static one it overlaps, so walking off the far side proves little: what
        //     climbing must change is whether the two collide at all.
        await Server.WaitAssertion(() => Assert.That(
            SEntMan.System<SharedPhysicsSystem>().IsHardCollidable(ToServer(Player), ToServer(Target!.Value)), Is.False,
            "someone climbing a sandbag wall shouldn't collide with it"));

        // Over it, and down the far side.
        await Move(DirectionFlag.East, 1f);
        Assert.Multiple(() =>
        {
            Assert.That(Delta(), Is.LessThan(0), "climbing should get the player over the sandbag wall");
            Assert.That(climbingComponent.IsClimbing, Is.False, "the player should stop climbing once past it");
        });
    }
}
