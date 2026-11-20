#nullable enable
using System.Linq;
using Content.Client.Eye.Blinking;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Klovnmed;
using Content.Shared._KS14.Klovnmed.Dismemberment;
using Content.Shared.Actions.Components;
using Content.Shared.Body;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinking;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Blinking;

/// <summary>
///     Eyelids are layers on the holder's sprite, added by the eyes organ, so they have to follow the eyes when the
///         organ carrying them is cut off and put back, hide under whatever covers the eyes, and stop blinking on their
///         own when periodic blinking is turned off.
/// </summary>
[TestFixture]
[TestOf(typeof(EyeBlinkingSystem)), TestOf(typeof(ClientEyeBlinkingSystem))]
public sealed class KsEyeBlinkingTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId HumanProto = "MobHuman";

    /// <summary>
    ///     A mob with eyes that blink, and the eyes organ itself, on the server.
    /// </summary>
    private async Task<(EntityUid Body, EntityUid Eyes)> SpawnBlinkingMob()
    {
        var testMap = await Pair.CreateTestMap();

        EntityUid bodyUid = default;
        await Server.WaitPost(() => bodyUid = SEntMan.SpawnEntity(HumanProto, testMap.GridCoords));
        await RunTicksSync(10);

        EntityUid eyesUid = default;
        await Server.WaitAssertion(() =>
        {
            var bodyComponent = SEntMan.GetComponent<BodyComponent>(bodyUid);
            eyesUid = bodyComponent.RecursiveChildUids.FirstOrDefault(SEntMan.HasComponent<EyeBlinkingComponent>);
            Assert.That(eyesUid, Is.Not.EqualTo(EntityUid.Invalid), "precondition: the mob has blinking eyes");
        });

        return (bodyUid, eyesUid);
    }

    /// <summary>
    ///     The client's eyelid layers on the holder, and whether each is shown.
    /// </summary>
    private async Task<bool[]> ClientEyelidVisibility(EntityUid serverBodyUid, EntityUid serverEyesUid)
    {
        var clientBodyUid = ToClientUid(serverBodyUid);
        var clientEyesUid = ToClientUid(serverEyesUid);
        var visibility = Array.Empty<bool>();

        await Client.WaitPost(() =>
        {
            var spriteSystem = CEntMan.System<SpriteSystem>();
            var blinkingComponent = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid);
            var spriteComponent = CEntMan.GetComponent<SpriteComponent>(clientBodyUid);

            visibility = blinkingComponent.Eyelids
                .Select(eyelid => spriteSystem.LayerMapTryGet((clientBodyUid, spriteComponent), eyelid.LayerKey, out var index, logMissing: false)
                    ? (bool?)spriteComponent[index].Visible
                    : null)
                .Where(visible => visible != null)
                .Select(visible => visible!.Value)
                .ToArray();
        });

        return visibility;
    }

    [Test]
    public async Task TestEyelidsAndActionFollowTheEyesOffAndBackOn()
    {
        var (bodyUid, eyesUid) = await SpawnBlinkingMob();
        var clientBodyUid = ToClientUid(bodyUid);
        var clientEyesUid = ToClientUid(eyesUid);

        Assert.That(await ClientEyelidVisibility(bodyUid, eyesUid), Is.Not.Empty, "precondition: the client gave the holder eyelid layers");

        // Whatever carries the eyes: a head on a human, but nothing here assumes it.
        EntityUid carrierUid = default;
        EntityUid carrierParentUid = default;
        EntityUid oldActionUid = default;
        await Server.WaitAssertion(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            carrierUid = transformSystem.GetParentUid(eyesUid) == bodyUid ? eyesUid : transformSystem.GetParentUid(eyesUid);
            carrierParentUid = transformSystem.GetParentUid(carrierUid);

            var blinkingComponent = SEntMan.GetComponent<EyeBlinkingComponent>(eyesUid);
            Assert.That(blinkingComponent.EyeToggleActionEntity, Is.Not.Null, "precondition: the holder can close its eyes");
            oldActionUid = blinkingComponent.EyeToggleActionEntity!.Value;

            // Shut them, so we can see that this doesn't outlive the eyes leaving.
            SEntMan.EventBus.RaiseLocalEvent(bodyUid, new ToggleEyesActionEvent { Performer = bodyUid });
            Assert.That(blinkingComponent.Status & BlinkStatus.EyesClosed, Is.EqualTo(BlinkStatus.EyesClosed), "precondition: the eyes closed");
            Assert.That(SEntMan.GetComponent<BlindableComponent>(bodyUid).IsBlind, Is.True, "precondition: closed eyes blind the holder");

            SEntMan.System<DismembermentSystem>().DismemberPart(bodyUid, carrierUid);
        });
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var blinkingComponent = SEntMan.GetComponent<EyeBlinkingComponent>(eyesUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(blinkingComponent.EyeToggleActionEntity, Is.Null, "The eyes kept their action after leaving the body.");
                Assert.That(SEntMan.TryGetComponent<ActionComponent>(oldActionUid, out var actionComponent) && actionComponent.AttachedEntity == bodyUid,
                    Is.False, "The body kept the close-eyes action without its eyes.");
                Assert.That(blinkingComponent.Status & BlinkStatus.EyesClosed, Is.EqualTo(BlinkStatus.Normal), "The eyes stayed shut by an action their old holder can no longer use.");
            }
        });

        await Client.WaitAssertion(() =>
        {
            var blinkingComponent = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid);
            var spriteComponent = CEntMan.GetComponent<SpriteComponent>(clientBodyUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(blinkingComponent.Eyelids, Is.Empty, "The client still tracks eyelids for eyes that left the body.");
                Assert.That(blinkingComponent.Body, Is.Null, "The client's eyes still point at the body they left.");
                Assert.That(CEntMan.System<SpriteSystem>().LayerMapTryGet((clientBodyUid, spriteComponent), $"{ClientEyeBlinkingSystem.LayerPrefix}-0", out _, logMissing: false),
                    Is.False, "The body kept eyelid layers without its eyes.");
            }
        });

        await Server.WaitAssertion(() =>
        {
            var containerSystem = SEntMan.System<SharedContainerSystem>();
            var container = containerSystem.GetContainer(carrierParentUid, BodyHierarchySystem.ConstContainerId);
            Assert.That(containerSystem.Insert(carrierUid, container), Is.True, "precondition: the carrier went back on");
        });
        await RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var blinkingComponent = SEntMan.GetComponent<EyeBlinkingComponent>(eyesUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(blinkingComponent.EyeToggleActionEntity, Is.Not.Null, "The holder got its eyes back without the close-eyes action.");
                Assert.That(SEntMan.GetComponent<BlindableComponent>(bodyUid).IsBlind, Is.False, "The holder got its eyes back and is still blind.");
            }
        });

        Assert.That(await ClientEyelidVisibility(bodyUid, eyesUid), Is.Not.Empty, "The client didn't give the eyelid layers back.");
    }

    [Test]
    public async Task TestEyelidsHideUnderWhatCoversTheEyes()
    {
        var (bodyUid, eyesUid) = await SpawnBlinkingMob();

        // Shut, so the eyelids are meant to show.
        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(bodyUid, new ToggleEyesActionEvent { Performer = bodyUid }));
        await RunTicksSync(10);

        Assert.That(await ClientEyelidVisibility(bodyUid, eyesUid), Is.Not.Empty.And.All.True, "precondition: shut eyes show their eyelids");

        HumanoidVisualLayers hidingLayer = default;
        await Server.WaitPost(() =>
        {
            hidingLayer = SEntMan.GetComponent<EyeBlinkingComponent>(eyesUid).KsHidingLayers.First();
            SEntMan.System<SharedHideableHumanoidLayersSystem>().SetLayerOcclusion(bodyUid, hidingLayer, hidden: true, SlotFlags.MASK);
        });
        await RunTicksSync(10);

        Assert.That(await ClientEyelidVisibility(bodyUid, eyesUid), Is.Not.Empty.And.All.False, $"The eyelids showed over a hidden {hidingLayer} layer.");

        await Server.WaitPost(() =>
            SEntMan.System<SharedHideableHumanoidLayersSystem>().SetLayerOcclusion(bodyUid, hidingLayer, hidden: false, SlotFlags.MASK));
        await RunTicksSync(10);

        Assert.That(await ClientEyelidVisibility(bodyUid, eyesUid), Is.Not.Empty.And.All.True, "The eyelids didn't come back when the layer was shown again.");
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestPeriodicBlinkingFollowsTheCVar(bool periodic)
    {
        await OverrideCVar(Side.Server, KsCCVars.BlinkingPeriodic, periodic);

        var (bodyUid, eyesUid) = await SpawnBlinkingMob();
        var clientEyesUid = ToClientUid(eyesUid);
        var clientTiming = Client.ResolveDependency<IGameTiming>();

        // Due now, so the next client update either starts a blink or puts it off.
        await Client.WaitPost(() => CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid).NextBlinkingTime = clientTiming.CurTime);
        await RunTicksSync(1);

        await Client.WaitAssertion(() =>
        {
            var blinkingComponent = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid);
            Assert.That(blinkingComponent.Status, Is.EqualTo(BlinkStatus.Normal), "precondition: the eyes are free to blink");

            if (periodic)
                Assert.That(blinkingComponent.BlinkInProgress, Is.True, "The eyes didn't blink when one was due.");
            else
            {
                Assert.That(blinkingComponent.BlinkInProgress, Is.False, "The eyes blinked on their own with periodic blinking off.");
                Assert.That(blinkingComponent.NextBlinkingTime, Is.GreaterThan(clientTiming.CurTime),
                    "With periodic blinking off, the due blink was left due, so it fires the moment it is turned on.");
            }
        });
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task TestEyesOnDeathFollowTheDatafield(bool closeOnDeath)
    {
        var (bodyUid, eyesUid) = await SpawnBlinkingMob();
        var clientEyesUid = ToClientUid(eyesUid);

        await Server.WaitPost(() =>
        {
            var blinkingComponent = SEntMan.GetComponent<EyeBlinkingComponent>(eyesUid);
            blinkingComponent.KsCloseEyesOnDeath = closeOnDeath;
            SEntMan.Dirty(eyesUid, blinkingComponent);
        });
        await RunTicksSync(5);

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid).KsCloseEyesOnDeath, Is.EqualTo(closeOnDeath),
                "precondition: the datafield reached the client"));

        if (closeOnDeath)
        {
            // Die with the eyes open, so closing them is the datafield's doing and not a blink caught half-way.
            await Client.WaitPost(() =>
            {
                var blinkingComponent = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid);
                blinkingComponent.NextBlinkingTime = TimeSpan.MaxValue;
            });

            var allOpen = false;
            for (var tick = 0; tick < 300 && !allOpen; tick++)
            {
                await RunTicksSync(1);
                await Client.WaitPost(() =>
                {
                    var blinkingComponent = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid);
                    allOpen = !blinkingComponent.BlinkInProgress && blinkingComponent.Eyelids.All(eyelid => !eyelid.IsClosed);
                });
            }
            Assert.That(allOpen, Is.True, "precondition: the eyes are open and not blinking");
        }
        else
        {
            // Die mid-blink, with at least one eyelid shut, so an open-eyed death has a blink to finish. Blink does
            //      nothing while one is still finishing, so keep asking until one starts and shuts an eyelid.
            var anyShut = false;
            for (var tick = 0; tick < 300 && !anyShut; tick++)
            {
                await Client.WaitPost(() =>
                {
                    var blinkingComponent = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid);
                    CEntMan.System<ClientEyeBlinkingSystem>().Blink((clientEyesUid, blinkingComponent));
                });
                await RunTicksSync(1);
                await Client.WaitPost(() => anyShut = CEntMan.GetComponent<EyeBlinkingComponent>(clientEyesUid).Eyelids.Any(eyelid => eyelid.IsClosed));
            }
            Assert.That(anyShut, Is.True, "precondition: an eyelid shut during the blink");
        }

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(bodyUid, MobState.Dead));
        await RunTicksSync(10);

        var visibility = await ClientEyelidVisibility(bodyUid, eyesUid);
        if (closeOnDeath)
            Assert.That(visibility, Is.Not.Empty.And.All.True, "The eyes stayed open on death with KsCloseEyesOnDeath set.");
        else
            Assert.That(visibility, Is.Not.Empty.And.All.False, "An eyelid froze shut on an open-eyed death.");
    }
}
