#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Client._KS14.Storage.ClosetDoor;
using Content.Client.Lock.Visualizers;
using Content.Client.Storage.Visualizers;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Storage.EntitySystems;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Storage;

/// <summary>
///     Closet doors swinging open and shut on the client, as tgstation's do.
/// </summary>
[TestOf(typeof(KsClosetDoorSystem))]
public sealed class KsClosetDoorTest : InteractionTest
{
    private static readonly EntProtoId ClosetPrototype = "ClosetMaintenance";
    private static readonly EntProtoId LockerPrototype = "LockerSteel";
    private static readonly EntProtoId ItemPrototype = "Crowbar";

    private const string LockLayerId = "enum.LockVisualLayers.Lock";

    private EntityUid ClientCloset => CTarget!.Value;

    private KsClosetDoorVisualsComponent ClientDoor => CEntMan.GetComponent<KsClosetDoorVisualsComponent>(ClientCloset);

    private bool LayerVisible(Enum layerKey)
    {
        var spriteComponent = CEntMan.GetComponent<SpriteComponent>(ClientCloset);
        var spriteSystem = CEntMan.System<SpriteSystem>();
        return spriteComponent[spriteSystem.LayerMapGet((ClientCloset, spriteComponent), layerKey)].Visible;
    }

    private bool DoorLayerVisible()
    {
        return LayerVisible(StorageVisualLayers.Door);
    }

    private async Task SetOpen(bool open)
    {
        await Server.WaitPost(() =>
        {
            var storageSystem = SEntMan.System<SharedEntityStorageSystem>();
            if (open)
                storageSystem.OpenStorage(STarget!.Value);
            else
                storageSystem.CloseStorage(STarget!.Value);
        });
        await RunTicks(3);
    }

    /// <summary>
    ///     Waits out a swing. It runs on real time, which ticks don't advance by much in a test.
    /// </summary>
    private async Task WaitOutSwing()
    {
        TimeSpan duration = default;
        await Client.WaitPost(() => duration = ClientDoor.Duration);
        await Task.Delay(duration + TimeSpan.FromMilliseconds(100));
        await RunTicks(3);
    }

    /// <summary>
    ///     Opening swings the door out, then leaves it drawn at its full angle. Closing swings it back, and the shut
    ///         door on the closet's sprite only comes back once the swing is over: before then, the overlay's door is
    ///         the only one drawn.
    /// </summary>
    [Test]
    public async Task TestDoorSwingsOpenAndShut()
    {
        await SpawnTarget(ClosetPrototype);
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(ClientDoor.Open, Is.False);
            Assert.That(doorSystem.IsAnimating(ClientCloset), Is.False);
            Assert.That(ClientDoor.DoorDrawn, Is.False);
            Assert.That(DoorLayerVisible(), Is.True);
        });

        await SetOpen(true);
        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(ClientDoor.Open, Is.True);
            Assert.That(doorSystem.IsAnimating(ClientCloset), Is.True, "opening didn't start a swing");
            Assert.That(ClientDoor.DoorDrawn, Is.True);
            Assert.That(DoorLayerVisible(), Is.False, "the sprite's shut door is still shown on an open closet");
            Assert.That(doorSystem.GetDoorAngle((ClientCloset, ClientDoor)), Is.InRange(0f, ClientDoor.MaxAngle));
        });

        await WaitOutSwing();
        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(doorSystem.IsAnimating(ClientCloset), Is.False, "the swing never finished");
            Assert.That(ClientDoor.DoorDrawn, Is.True, "an open closet's door isn't drawn");
            Assert.That(doorSystem.GetDoorAngle((ClientCloset, ClientDoor)), Is.EqualTo(ClientDoor.MaxAngle));
            Assert.That(DoorLayerVisible(), Is.False);
        });

        await SetOpen(false);
        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(ClientDoor.Open, Is.False);
            Assert.That(doorSystem.IsClosing((ClientCloset, ClientDoor)), Is.True, "closing didn't start a swing");
            Assert.That(ClientDoor.DoorDrawn, Is.True);
            Assert.That(DoorLayerVisible(), Is.False, "the shut door was shown while the door was still swinging shut");
        });

        await WaitOutSwing();
        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(doorSystem.IsAnimating(ClientCloset), Is.False, "the swing never finished");
            Assert.That(ClientDoor.DoorDrawn, Is.False);
            Assert.That(doorSystem.GetDoorAngle((ClientCloset, ClientDoor)), Is.Zero);
            Assert.That(DoorLayerVisible(), Is.True, "the shut door didn't come back after the swing");
        });
    }

    /// <summary>
    ///     A locker's lock comes off its sprite while the door is drawn, to be drawn on the door instead, and goes back
    ///         on once the door is shut. On every tick of the swing shut, some door is drawn: the overlay's while the
    ///         door swings, then the sprite's, with no tick in between showing neither.
    /// </summary>
    [Test]
    public async Task TestLockSwingsWithTheDoor()
    {
        await SpawnTarget(LockerPrototype);
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            Assert.That(LayerVisible(LockVisualLayers.Lock), Is.True);
            Assert.That(ClientDoor.ShutLayerVisibility.GetValueOrDefault(LockLayerId), Is.True,
                "the lock shown on the shut locker wasn't noted, so it won't be drawn on the door");
        });

        await SetOpen(true);
        await Client.WaitAssertion(() =>
        {
            Assert.That(LayerVisible(LockVisualLayers.Lock), Is.False, "the lock stayed on the sprite while the door swung away");
            Assert.That(ClientDoor.HiddenLayers, Does.Contain(LockLayerId));
            Assert.That(ClientDoor.ShutLayerVisibility.GetValueOrDefault(LockLayerId), Is.True,
                "opening forgot the lock was shown, so it won't be drawn on the door");
        });

        await WaitOutSwing();

        TimeSpan duration = default;
        await Client.WaitPost(() => duration = ClientDoor.Duration);

        await SetOpen(false);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var sawSwing = false;
        while (stopwatch.Elapsed < duration + TimeSpan.FromMilliseconds(200))
        {
            await Task.Delay(5);
            await RunTicks(1);
            await Client.WaitAssertion(() =>
            {
                var animating = CEntMan.System<KsClosetDoorSystem>().IsAnimating(ClientCloset);
                sawSwing |= animating;
                Assert.That(animating || DoorLayerVisible(), Is.True,
                    $"{stopwatch.Elapsed.TotalMilliseconds:F0}ms into the swing shut, neither the overlay's door nor the sprite's was drawn");
                Assert.That(animating || LayerVisible(LockVisualLayers.Lock), Is.True,
                    $"{stopwatch.Elapsed.TotalMilliseconds:F0}ms into the swing shut, the lock was on neither the door nor the sprite");
            });
        }

        await Client.WaitAssertion(() =>
        {
            Assert.That(sawSwing, Is.True, "the door never swung shut");
            Assert.That(CEntMan.System<KsClosetDoorSystem>().IsAnimating(ClientCloset), Is.False, "the swing never finished");
            Assert.That(ClientDoor.HiddenLayers, Is.Empty);
        });
    }

    /// <summary>
    ///     The overlay draws later in the frame than the system decides whether a swing is over. A swing that ends
    ///         between the two must still be drawn at the angle the system saw, since the sprite's own door stays hidden
    ///         until the next frame: drawn at the live time's angle, 0, the closet flickered doorless for a frame.
    /// </summary>
    [Test]
    public async Task TestSwingEndingBeforeTheDrawKeepsItsAngle()
    {
        await SpawnTarget(ClosetPrototype);
        await SetOpen(true);
        await WaitOutSwing();

        TimeSpan duration = default;
        await Client.WaitPost(() => duration = ClientDoor.Duration);

        await SetOpen(false);

        float frameAngle = default;
        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(doorSystem.IsClosing((ClientCloset, ClientDoor)), Is.True, "closing didn't start a swing");
            frameAngle = doorSystem.GetDoorAngle((ClientCloset, ClientDoor));
        });

        // Past the swing's end in real time, with no frame since.
        await Task.Delay(duration + TimeSpan.FromMilliseconds(100));
        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(doorSystem.IsAnimating(ClientCloset), Is.True, "the swing ended without a frame, so this test checks nothing");
            Assert.That(DoorLayerVisible(), Is.False);
            Assert.That(doorSystem.GetDoorAngle((ClientCloset, ClientDoor)), Is.EqualTo(frameAngle),
                "the door's angle moved on between frames, so the overlay can draw a swing the system hasn't seen end");
            Assert.That(frameAngle, Is.GreaterThan(0f), "the swing was already shut when it started");
        });
    }

    /// <summary>
    ///     Going into a container zeroes an entity's rotation. What a closing closet draws inside it still faces the
    ///         way it did before it went in.
    /// </summary>
    [Test]
    public async Task TestContentsKeepTheirFacing()
    {
        await SpawnTarget(ClosetPrototype);
        await SetOpen(true);

        NetEntity itemNetEntity = default;
        Angle facing = default;
        await Server.WaitPost(() =>
        {
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            var itemUid = SEntMan.SpawnEntity(ItemPrototype, SEntMan.GetCoordinates(TargetCoords));
            transformSystem.SetLocalRotation(itemUid, Angle.FromDegrees(90));
            facing = transformSystem.GetWorldRotation(itemUid);
            itemNetEntity = SEntMan.GetNetEntity(itemUid);
        });
        await RunTicks(3);

        await SetOpen(false);
        await Client.WaitAssertion(() =>
        {
            var itemUid = CEntMan.GetEntity(itemNetEntity);
            var transformSystem = CEntMan.System<SharedTransformSystem>();
            Assert.That(CEntMan.GetComponent<TransformComponent>(itemUid).ParentUid, Is.EqualTo(ClientCloset), "the item didn't go in");
            Assert.That(transformSystem.GetWorldRotation(itemUid).EqualsApprox(facing), Is.False,
                "going in no longer changes the item's rotation, so this test checks nothing");
            Assert.That(CEntMan.System<KsClosetDoorSystem>().GetContentWorldRotation(itemUid).EqualsApprox(facing, 0.001), Is.True,
                "the item inside the closet isn't drawn facing the way it went in");
        });
    }

    /// <summary>
    ///     A closet already open when the client first sees it is drawn open straight away, without swinging, as
    ///         tgstation does for closets open at round start.
    /// </summary>
    [Test]
    public async Task TestAlreadyOpenClosetDoesNotSwing()
    {
        await Server.WaitPost(() =>
        {
            var closetUid = SEntMan.SpawnEntity(ClosetPrototype, SEntMan.GetCoordinates(TargetCoords));
            SEntMan.System<SharedEntityStorageSystem>().OpenStorage(closetUid);
            Target = SEntMan.GetNetEntity(closetUid);
        });
        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            Assert.That(ClientDoor.Open, Is.True);
            Assert.That(doorSystem.IsAnimating(ClientCloset), Is.False, "a closet that arrived open swung open");
            Assert.That(ClientDoor.DoorDrawn, Is.True);
            Assert.That(doorSystem.GetDoorAngle((ClientCloset, ClientDoor)), Is.EqualTo(ClientDoor.MaxAngle));
        });
    }

    /// <summary>
    ///     Every state an entity storage's visuals name is in its RSI. Closets whose door swings name no open door
    ///         state, and have the back of their door for the overlay to draw.
    /// </summary>
    [Test]
    public async Task TestEveryClosetStateExists()
    {
        await Client.WaitAssertion(() =>
        {
            var factory = CEntMan.ComponentFactory;
            var doorSystem = CEntMan.System<KsClosetDoorSystem>();
            var failures = new List<string>();
            var checkedSwinging = 0;

            foreach (var prototype in Client.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.Abstract ||
                    !prototype.TryComp<EntityStorageVisualsComponent>(out var visualsComponent, factory) ||
                    !prototype.TryComp<SpriteComponent>(out var spriteComponent, factory) ||
                    spriteComponent.BaseRSI is not { } rsi)
                    continue;

                void CheckState(string? state, string field)
                {
                    if (state != null && !rsi.TryGetState(state, out _))
                        failures.Add($"{prototype.ID}: {field} '{state}' is not in {rsi.Path}");
                }

                CheckState(visualsComponent.StateBaseClosed, "stateBaseClosed");
                CheckState(visualsComponent.StateBaseOpen, "stateBaseOpen");
                CheckState(visualsComponent.StateDoorOpen, "stateDoorOpen");
                CheckState(visualsComponent.StateDoorClosed, "stateDoorClosed");

                if (!prototype.TryComp<KsClosetDoorVisualsComponent>(out var doorComponent, factory) ||
                    doorComponent.Duration <= TimeSpan.Zero)
                    continue;

                checkedSwinging++;
                foreach (var layerId in doorComponent.Layers.Keys)
                {
                    if (!doorSystem.TryGetMountedLayer((EntityUid.Invalid, spriteComponent), layerId, out _))
                        failures.Add($"{prototype.ID}: its door carries the layer '{layerId}', which its sprite doesn't have");
                }

                if (visualsComponent.StateDoorOpen != null)
                    failures.Add($"{prototype.ID}: its door swings, but it also has an open door state, which would be drawn over it");

                if (visualsComponent.StateDoorClosed == null)
                    failures.Add($"{prototype.ID}: its door swings, but it has no door state to swing");

                CheckState(visualsComponent.StateBaseClosed + KsClosetDoorVisualsComponent.BackStateSuffix, "the back of its door");
            }

            Assert.That(checkedSwinging, Is.GreaterThan(0), "no closet has a swinging door");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });
    }
}
