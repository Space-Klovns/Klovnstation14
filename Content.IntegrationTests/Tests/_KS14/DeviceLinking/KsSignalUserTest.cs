#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.KillCredit;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.Interaction.Events;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.DeviceLinking;

/// <summary>
///     Whoever presses a signaller answers for what its signal sets off (<see cref="KsKillCreditSystem"/>), but is not
///         the user of the trigger it sets off: no client predicts a signal, and a trigger's user is whose client is
///         taken to have predicted its sounds already.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedDeviceLinkSystem))]
public sealed class KsSignalUserTest : GameTest
{
    private const string SoundPath = "/Audio/Effects/double_beep.ogg";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _signaller = "KsSignalUserTestSignaller";
    private readonly EntProtoId _beeper = "KsSignalUserTestBeeper";
    private readonly EntProtoId _presser = "KsSignalUserTestPresser";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: KsSignalUserTestSignaller
  components:
  - type: Signaller
  - type: DeviceLinkSource
    ports:
    - Pressed

- type: entity
  id: KsSignalUserTestBeeper
  components:
  - type: TriggerOnSignal
  - type: EmitSoundOnTrigger
    predicted: true
    sound:
      path: {SoundPath}

- type: entity
  id: KsSignalUserTestPresser
";

    [Test]
    public async Task PresserIsCreditedButStillHearsWhatTheSignalSetsOff()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid signallerUid = default;
        EntityUid presserUid = default;
        await Server.WaitPost(() =>
        {
            signallerUid = SSpawnAtPosition(_signaller, testMap.GridCoords);
            presserUid = SSpawnAtPosition(_presser, testMap.GridCoords);
            var beeperUid = SSpawnAtPosition(_beeper, testMap.GridCoords);

            Server.System<DeviceLinkSystem>().SaveLinks(null, signallerUid, beeperUid, [("Pressed", "Trigger")]);

            var useEvent = new UseInHandEvent(presserUid);
            SEntMan.EventBus.RaiseLocalEvent(signallerUid, useEvent);
            Assert.That(useEvent.Handled, Is.True, "the signaller should have been pressed");
        });

        // What each of the beeper's sounds leaves out, by audio entity: a sound despawns once it has played.
        var heard = new Dictionary<EntityUid, EntityUid?>();
        for (var tick = 0; tick < 5; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var query = SEntMan.AllEntityQueryEnumerator<AudioComponent>();
                while (query.MoveNext(out var audioUid, out var audioComponent))
                {
                    if (audioComponent.FileName == SoundPath)
                        heard.TryAdd(audioUid, audioComponent.ExcludedEntity);
                }
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(heard, Has.Count.EqualTo(1), "the signal should have set the beeper off");
                Assert.That(heard.Values.FirstOrDefault(), Is.Not.EqualTo(presserUid),
                    "the sound should not leave the presser out: their client never predicted it");
                Assert.That(SEntMan.GetComponent<DeviceLinkSourceComponent>(signallerUid).KsSignalUser, Is.EqualTo(presserUid),
                    "the signaller should remember who pressed it, to answer for what its signal sets off");
            });
        });
    }
}
