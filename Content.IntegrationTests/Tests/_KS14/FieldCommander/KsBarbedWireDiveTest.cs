#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Shared._KS14.Movement.StopsDives;
using Content.Shared.Actions;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Barbed wire as it is in the game, and a player as one is: a dive into the wire, done as the player's client does
///         it, ends at the wire, whether standing right beside it or off a run. Something thrown still goes over.
/// </summary>
[TestOf(typeof(KsStopsDivesSystem))]
public sealed class KsBarbedWireDiveTest : MovementTest
{
    private static readonly EntProtoId BarbedWire = "KsBarbedWire";
    private static readonly EntProtoId ThrownItem = "Crowbar";

    protected override int Tiles => 6;

    /// <summary>
    ///     Where the player's middle is, in tiles east of where it started.
    /// </summary>
    private float PlayerX()
    {
        return SEntMan.GetComponent<TransformComponent>(SPlayer).LocalPosition.X - SEntMan.GetCoordinates(PlayerCoords).X;
    }

    /// <summary>
    ///     How near the middle of a piece of wire the player's middle is when its body first touches the wire's: half
    ///         the wire's fixture, and the player's own radius. Read from the two, not restated.
    /// </summary>
    private float ContactDistance(EntityUid wireUid)
    {
        var physicsSystem = SEntMan.System<SharedPhysicsSystem>();
        return physicsSystem.GetWorldAABB(wireUid).Width / 2f + physicsSystem.GetHardAABB(SPlayer).Width / 2f;
    }

    /// <summary>
    ///     Has the player's client dive east, standing or after holding the key east for <paramref name="runUp"/> seconds
    ///         and still holding it, and runs until the dive is over and the player has come to rest.
    /// </summary>
    private async Task DiveEast(float runUp)
    {
        // Standing, a dive goes the way the player faces; on the move, the way they are going.
        await Server.WaitPost(() => Transform.SetLocalRotation(SPlayer, Direction.East.ToAngle()));
        await RunTicks(5);

        if (runUp > 0f)
        {
            await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
            await RunSeconds(runUp);
        }

        NetEntity diveAction = default;
        await Server.WaitAssertion(() =>
        {
            var actionUid = SEntMan.GetComponent<JumpAbilityComponent>(SPlayer).ActionEntity;
            Assert.That(actionUid, Is.Not.Null, "the player should have a dive");
            diveAction = SEntMan.GetNetEntity(actionUid!.Value);
        });

        await Client.WaitPost(() => CEntMan.RaisePredictiveEvent(new RequestPerformActionEvent(diveAction)));
        await RunTicks(3);

        if (runUp > 0f)
            await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);

        await RunSeconds(4f);
    }

    /// <param name="wireTile">How many tiles east of the player the wire is.</param>
    /// <param name="runUp">How long the player runs at it before diving.</param>
    [TestCase(1, 0f, Description = "standing right beside the wire")]
    [TestCase(2, 0.15f, Description = "off a short run")]
    [TestCase(3, 0.35f, Description = "off a run")]
    public async Task ADiveIntoBarbedWireEndsAtIt(int wireTile, float runUp)
    {
        var wireCoordinates = SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(wireTile, 0f));
        var wire = await SpawnEntity(BarbedWire, wireCoordinates);
        // A piece behind it, as wire is laid: nobody should come out the far side of the first.
        await SpawnEntity(BarbedWire, wireCoordinates.Offset(new Vector2(1f, 0f)));

        await DiveEast(runUp);

        await Server.WaitAssertion(() =>
        {
            var contactX = wireTile - ContactDistance(wire);
            // A dive covers a third of a tile a tick, and the contact is found the tick after it starts.
            Assert.That(PlayerX(), Is.InRange(contactX - 0.05f, contactX + 0.45f),
                $"the dive should end where the player first touched the wire, {contactX} east of the start");
            Assert.That(SEntMan.HasComponent<ActiveLeaperComponent>(SPlayer), Is.False, "the dive should be over");
        });
    }

    /// <summary>
    ///     The same dive with nothing in the way goes further than the wire would have been: the wire is what stops it.
    /// </summary>
    [Test]
    public async Task ADiveOverOpenFloorCarriesOn()
    {
        await DiveEast(0f);
        await Server.WaitAssertion(() =>
            Assert.That(PlayerX(), Is.GreaterThan(1f), "a dive with nothing in the way should carry past the next tile's middle"));
    }

    [Test]
    public async Task SomethingThrownGoesOverBarbedWire()
    {
        var origin = SEntMan.GetCoordinates(PlayerCoords);
        await SpawnEntity(BarbedWire, origin.Offset(new Vector2(2f, 0f)));
        var thrown = await SpawnEntity(ThrownItem, origin.Offset(new Vector2(1f, 0f)));

        await Server.WaitPost(() =>
            SEntMan.System<ThrowingSystem>().TryThrow(thrown, origin.Offset(new Vector2(4f, 0f)), baseThrowSpeed: 10f));
        await RunSeconds(4f);

        await Server.WaitAssertion(() =>
        {
            var x = SEntMan.GetComponent<TransformComponent>(thrown).LocalPosition.X - origin.X;
            Assert.That(x, Is.GreaterThan(2.5f), "something thrown should go over the wire");
        });
    }
}
