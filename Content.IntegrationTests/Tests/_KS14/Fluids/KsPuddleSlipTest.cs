#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Fluids;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Slippery;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluids;

/// <summary>
///     A puddle's second fixture, <see cref="KsPuddleTouchSystem.TouchFixtureId"/>, changes nothing about slipping on it.
/// </summary>
[TestOf(typeof(KsPuddleTouchSystem))]
public sealed class KsPuddleSlipTest : MovementTest
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    /// <summary>
    ///     Running across a puddle slips the runner once; leaving it releases them, so running across it again slips
    ///         them once more.
    /// </summary>
    [Test]
    public async Task TestRunningAcrossPuddleSlipsOnce()
    {
        var playerCoordinates = SEntMan.GetCoordinates(PlayerCoords);
        EntityUid puddleUid = default;

        await Server.WaitPost(() =>
        {
            var puddleCoordinates = playerCoordinates.Offset(new Vector2(1f, 0f));
            var tileRef = SEntMan.System<SharedMapSystem>().GetTileRef(MapData.Grid.Owner, MapData.Grid.Comp, puddleCoordinates);

            var spilled = new Solution(WaterReagent.Id, FixedPoint2.New(30f));
            Assert.That(SEntMan.System<PuddleSystem>().TrySpillAt(tileRef, spilled, out puddleUid, sound: false, tileReact: false),
                "The puddle failed to spill.");
        });
        await RunTicks(5);

        Target = SEntMan.GetNetEntity(puddleUid);
        var stepTriggerComponent = SEntMan.GetComponent<StepTriggerComponent>(puddleUid);
        Assert.That(stepTriggerComponent.Active, "The water puddle is not slippery, so this proves nothing.");

        await AssertFiresEvent<SlipEvent>(async () => await Move(DirectionFlag.East, 1f), count: 1);
        AssertEvent<SlipEvent>(predicate: @event => @event.Slipped == SPlayer);
        AssertComp<KnockedDownComponent>(true, Player);

        // Off the puddle, and back on one's feet.
        await Server.WaitPost(() =>
        {
            Transform.SetCoordinates(SPlayer, playerCoordinates.Offset(new Vector2(-2f, 0f)));
            SEntMan.System<SharedPhysicsSystem>().WakeBody(SPlayer);
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(stepTriggerComponent.Colliding, Does.Not.Contain(SPlayer), "The player is still colliding with the puddle after leaving it.");
                Assert.That(stepTriggerComponent.CurrentlySteppedOn, Does.Not.Contain(SPlayer), "The player is still stepping on the puddle after leaving it.");
            }

            var stunSystem = SEntMan.System<SharedStunSystem>();
            stunSystem.TryUnstun(SPlayer);
            stunSystem.ForceStandUp(SPlayer);
        });
        await RunTicks(5);
        AssertComp<KnockedDownComponent>(false, Player);

        await AssertFiresEvent<SlipEvent>(async () => await Move(DirectionFlag.East, 1.5f), count: 1);
        AssertEvent<SlipEvent>(predicate: @event => @event.Slipped == SPlayer);
    }
}
