#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Lawnmower;
using Content.Shared.Buckle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Lawnmower;

/// <summary>
///     Driving a mower without stopping plays its drive clips back to back, with no silence between them.
/// </summary>
[TestOf(typeof(SharedKsLawnmowerBladesSystem))]
public sealed class KsLawnmowerDriveSoundTest : GameTest
{
    private readonly EntProtoId _rider = "KsLawnmowerDriveSoundTestRider";
    private readonly EntProtoId _mower = "KsLawnmowerDriveSoundTestMower";
    private readonly ProtoId<SoundCollectionPrototype> _driveSounds = "KsLawnmowerDriveSoundTestDrive";

    [TestPrototypes]
    private const string Prototypes = @"
- type: soundCollection
  id: KsLawnmowerDriveSoundTestDrive
  files:
  - /Audio/_KS14/Vehicles/Lawnmower/mowermove1.ogg

- type: entity
  id: KsLawnmowerDriveSoundTestRider
  components:
  - type: Buckle
  - type: Hands
    hands:
      hand_right:
        location: Right
    sortedHands:
    - hand_right
  - type: ComplexInteraction
  - type: StandingState
  - type: Eye
  - type: InputMover
  - type: MobMover
  - type: Physics
    bodyType: KinematicController

# Counts as driving at any speed, so it needn't move: only the held direction matters.
- type: entity
  id: KsLawnmowerDriveSoundTestMower
  components:
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Fixtures
    fixtures:
      blades:
        shape:
          !type:PhysShapeCircle
          radius: 0.4
        hard: false
  - type: Strap
  - type: Vehicle
    transferDamage: false
  - type: StrapVehicle
  - type: KsLawnmowerBlades
    minDrivingSpeed: 0
    driveSound:
      collection: KsLawnmowerDriveSoundTestDrive
    bumpSound:
      collection: KsLawnmowerDriveSoundTestDrive
    bladeSound:
      collection: KsLawnmowerDriveSoundTestDrive
    crushSound:
      collection: KsLawnmowerDriveSoundTestDrive
    bumpDamage: {}
    crushDamage: 0
";

    [Test]
    public async Task TestDriveClipsPlayBackToBack()
    {
        var entManager = Server.EntMan;
        var gameTiming = Server.ResolveDependency<IGameTiming>();
        var map = await Pair.CreateTestMap();
        EntityUid mowerUid = default;
        var clipLength = TimeSpan.Zero;

        await Server.WaitAssertion(() =>
        {
            mowerUid = entManager.SpawnEntity(_mower, map.MapCoords);
            var riderUid = entManager.SpawnEntity(_rider, map.MapCoords);
            Assert.That(entManager.System<SharedBuckleSystem>().TryBuckle(riderUid, riderUid, mowerUid), Is.True);
            Assert.That(entManager.GetComponent<VehicleComponent>(mowerUid).Operator, Is.EqualTo(riderUid));

            var clipPath = Server.ProtoMan.Index(_driveSounds).PickFiles[0];
            clipLength = entManager.System<SharedAudioSystem>().GetAudioLength(new ResolvedPathSpecifier(clipPath.ToString()));
            Assert.That(clipLength, Is.GreaterThan(gameTiming.TickPeriod * 4), "the test clip should last several ticks");

            entManager.System<SharedMoverController>().SetVelocityDirection(
                (mowerUid, entManager.GetComponent<InputMoverComponent>(mowerUid)), Direction.East, subTick: 0, enabled: true);
        });

        // Each change of NextDriveSound is a clip starting; collect when they start.
        var clipStarts = new List<TimeSpan>();
        var lastNextDriveSound = TimeSpan.Zero;
        var ticks = (int) Math.Ceiling(clipLength * 4 / gameTiming.TickPeriod);
        for (var i = 0; i < ticks; i++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var next = entManager.GetComponent<KsLawnmowerBladesComponent>(mowerUid).NextDriveSound;
                if (next != lastNextDriveSound)
                    clipStarts.Add(gameTiming.CurTime);
                lastNextDriveSound = next;
            });
        }

        Assert.That(clipStarts, Has.Count.GreaterThanOrEqualTo(3), "the drive sound should keep playing while driving");
        for (var i = 1; i < clipStarts.Count; i++)
        {
            var gap = clipStarts[i] - clipStarts[i - 1];
            Assert.That(gap, Is.LessThanOrEqualTo(clipLength),
                $"clip {i} started {gap.TotalMilliseconds:F0} ms after the last, which lasts {clipLength.TotalMilliseconds:F0} ms: there's silence between them");
            Assert.That(gap, Is.GreaterThan(clipLength - gameTiming.TickPeriod * 2),
                "nor should the next clip start well before the last one ends");
        }
    }
}
