#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._KS14.Mortar;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.Mortar;
using Content.Shared._KS14.Mortar;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     The sight in a mortar's window (<c>KsMortarSystem.Sight.cs</c>): opening it gives the player an eye at their aim
///         and subscribes them to it; closing it, walking out of its range or the mortar going takes both away; the aim is
///         kept to the range and refused when it is no number; and what is near the aim, far from the player, reaches
///         them only while it is open.
/// </summary>
[TestFixture]
[TestOf(typeof(KsMortarSystem))]
public sealed class KsMortarSightTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _mortar = "KsTestSightMortar";
    private readonly EntProtoId _blindMortar = "KsTestBlindMortar";
    private readonly EntProtoId _eye = "KsTestMortarSightEye";
    private readonly EntProtoId _marker = "KsTestMortarSightMarker";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestMortarSightMarker

- type: entity
  id: KsTestMortarSightEye
  components:
  - type: Transform
    gridTraversal: false
  - type: Eye
    drawFov: false

- type: entity
  id: KsTestSightMortar
  components:
  - type: Transform
    anchored: true
  - type: KsMortar
    shell: KsTestMortarSightMarker
    minRange: 30
    maxRange: 50
    sightRadius: 3
    sightEye: KsTestMortarSightEye
    sightMoveInterval: 0.5
  - type: UserInterface
    interfaces:
      enum.KsMortarUiKey.Key:
        type: KsMortarBoundUserInterface

- type: entity
  parent: KsTestSightMortar
  id: KsTestBlindMortar
  components:
  - type: KsMortar
    shell: KsTestMortarSightMarker
    minRange: 30
    maxRange: 50
    sightRadius: 0
    sightEye: KsTestMortarSightEye
";

    private EntityUid _mortarUid;
    private EntityUid _userUid;
    private TestMapData _map = default!;

    private int Ticks(float seconds) => (int) MathF.Ceiling(seconds * (float) Server.ResolveDependency<IGameTiming>().TickRate);

    private KsMortarComponent MortarComponent => SEntMan.GetComponent<KsMortarComponent>(_mortarUid);

    /// <summary>
    ///     A mortar at the origin and the player standing next to it, its window not yet open.
    /// </summary>
    private async Task SetUp(EntProtoId mortarId)
    {
        _map = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            _mortarUid = SEntMan.SpawnEntity(mortarId, new EntityCoordinates(_map.Grid.Owner, 0.5f, 0.5f));
            _userUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(_map.Grid.Owner, 1.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(ServerSession!, _userUid);
        });
        await Pair.RunTicksSync(10);
    }

    private async Task OpenWindow()
    {
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedUserInterfaceSystem>().TryOpenUi(_mortarUid, KsMortarUiKey.Key, _userUid), Is.True,
                "precondition: the window opens"));
        await Pair.RunTicksSync(10);
    }

    private int CountEyes()
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var metaDataComponent))
        {
            if (metaDataComponent.EntityPrototype?.ID == _eye.Id && metaDataComponent.EntityLifeStage < EntityLifeStage.Terminating)
                count++;
        }

        return count;
    }

    private KsMortarWindow? ClientWindow()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<KsMortarWindow>().FirstOrDefault(window => window.IsOpen);
    }

    /// <summary>
    ///     Opening the window gives the player their sight: an eye, which they are subscribed to and their window shows.
    /// </summary>
    private async Task<EntityUid> AssertSightOpen()
    {
        var eyeUid = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var eye = SEntMan.System<KsMortarSystem>().GetSightEye(MortarComponent, _userUid);
            Assert.That(eye, Is.Not.Null, "opening the window should give the player a sight");
            eyeUid = eye!.Value;
            Assert.That(SEntMan.EntityExists(eyeUid), Is.True);
            Assert.That(ServerSession!.ViewSubscriptions, Does.Contain(eyeUid), "the player should be subscribed to their sight's eye");
            Assert.That(CountEyes(), Is.EqualTo(1), "one eye for one player");
        });

        await Client.WaitAssertion(() =>
        {
            var window = ClientWindow();
            Assert.That(window, Is.Not.Null, "precondition: the window is open on the client");
            Assert.That(window!.SightBox.Visible, Is.True, "a mortar with a sight should show it");
            Assert.That(window.SightHasPicture, Is.True, "the window should show the view through the sight's eye");
        });

        return eyeUid;
    }

    private async Task AssertSightGone(EntityUid eyeUid, string why)
    {
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.ViewSubscriptions, Does.Not.Contain(eyeUid), $"{why}: the player should no longer be subscribed");
            Assert.That(SEntMan.Deleted(eyeUid), Is.True, $"{why}: the sight's eye should be deleted");
            Assert.That(CountEyes(), Is.Zero, $"{why}: no eye should be left behind");
            if (SEntMan.TryGetComponent<KsMortarComponent>(_mortarUid, out var mortarComponent))
                Assert.That(mortarComponent.Sights, Is.Empty, $"{why}: the mortar should hold no sight");
        });
    }

    [Test]
    public async Task ClosingTheWindowEndsTheSight()
    {
        await SetUp(_mortar);
        await OpenWindow();
        var eyeUid = await AssertSightOpen();

        await Server.WaitPost(() => SEntMan.System<SharedUserInterfaceSystem>().CloseUi(_mortarUid, KsMortarUiKey.Key, _userUid));
        await AssertSightGone(eyeUid, "the window closed");
    }

    [Test]
    public async Task WalkingAwayEndsTheSight()
    {
        await SetUp(_mortar);
        await OpenWindow();
        var eyeUid = await AssertSightOpen();

        await Server.WaitPost(() =>
            SEntMan.System<SharedTransformSystem>().SetCoordinates(_userUid, new EntityCoordinates(_map.MapUid, new Vector2(15.5f, 0.5f))));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedUserInterfaceSystem>().IsUiOpen(_mortarUid, KsMortarUiKey.Key, _userUid),
            Is.False, "precondition: out of its range, the window closes"));
        await AssertSightGone(eyeUid, "the player walked out of range");
    }

    /// <summary>
    ///     The mortar deleted (which closes its window too), or only its mortar component taken off (which does not).
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task LosingTheMortarEndsTheSight(bool deleteEntity)
    {
        await SetUp(_mortar);
        await OpenWindow();
        var eyeUid = await AssertSightOpen();

        await Server.WaitPost(() =>
        {
            if (deleteEntity)
                SEntMan.DeleteEntity(_mortarUid);
            else
                SEntMan.RemoveComponent<KsMortarComponent>(_mortarUid);
        });
        await AssertSightGone(eyeUid, deleteEntity ? "the mortar was deleted" : "the mortar lost its mortar component");
    }

    [Test]
    public async Task AMortarWithNoSightHasNoEye()
    {
        await SetUp(_blindMortar);
        await OpenWindow();

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<KsMortarSystem>().GetSightEye(MortarComponent, _userUid), Is.Null, "a sight radius of zero means no sight");
            Assert.That(CountEyes(), Is.Zero);
            Assert.That(ServerSession!.ViewSubscriptions, Is.Empty);
        });
        await Client.WaitAssertion(() =>
        {
            var window = ClientWindow();
            Assert.That(window, Is.Not.Null, "precondition: the window is open on the client");
            Assert.That(window!.SightBox.Visible, Is.False, "a mortar with no sight should not show one");
        });
    }

    /// <summary>
    ///     The eye goes where the player aims, through the real window and message; kept to the range, never moved for what
    ///         is no number, and no more often than its interval.
    /// </summary>
    [Test]
    public async Task TheSightFollowsTheAimWithinTheRange()
    {
        await SetUp(_mortar);
        await OpenWindow();
        var eyeUid = await AssertSightOpen();
        var system = SEntMan.System<KsMortarSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        Vector2 Offset() => transformSystem.GetWorldPosition(eyeUid) - transformSystem.GetWorldPosition(_mortarUid);

        float min = 0f, max = 0f, interval = 0f;
        await Server.WaitPost(() =>
        {
            min = MortarComponent.MinRange;
            max = MortarComponent.MaxRange;
            interval = (float) MortarComponent.SightMoveInterval.TotalSeconds;
        });

        // As a player would: typed into the window, sent by its interface.
        await Client.WaitPost(() =>
        {
            var window = ClientWindow()!;
            window.BearingEdit.SetText("90", invokeEvent: true);
            window.DistanceEdit.SetText(min.ToString(), invokeEvent: true);
        });
        await Pair.RunTicksSync(Ticks(interval) + 5);
        await Server.WaitAssertion(() => Assert.That(Vector2.Distance(Offset(), new Vector2(min, 0f)), Is.LessThan(0.05f),
            $"the sight should go where the window aims, east at {min}: {Offset()}"));

        await Pair.RunTicksSync(Ticks(interval) + 5);
        await Server.WaitAssertion(() =>
        {
            var mortar = new Entity<KsMortarComponent>(_mortarUid, MortarComponent);
            var before = Offset();
            Assert.That(system.TrySetSightAim(mortar, _userUid, new Angle(double.NaN), 40f), Is.False, "a bearing that is no number");
            Assert.That(system.TrySetSightAim(mortar, _userUid, Angle.Zero, float.PositiveInfinity), Is.False, "an endless distance");
            Assert.That(system.TrySetSightAim(mortar, _userUid, Angle.Zero, float.NaN), Is.False, "a distance that is no number");
            Assert.That(Offset(), Is.EqualTo(before), "nothing refused should move the eye");

            Assert.That(system.TrySetSightAim(mortar, _userUid, Angle.Zero, 1000f), Is.True);
            Assert.That(Vector2.Distance(Offset(), new Vector2(0f, max)), Is.LessThan(0.05f), $"past its range is kept to it: {Offset()}");

            // Straight after: held until its interval is up.
            Assert.That(system.TrySetSightAim(mortar, _userUid, Angle.FromDegrees(180), 1f), Is.True);
            Assert.That(Vector2.Distance(Offset(), new Vector2(0f, max)), Is.LessThan(0.05f), "an aim sent too soon should wait");
        });

        await Pair.RunTicksSync(Ticks(interval) + 5);
        await Server.WaitAssertion(() => Assert.That(Vector2.Distance(Offset(), new Vector2(0f, -min)), Is.LessThan(0.05f),
            $"once its interval is up the waiting aim should be taken, short of its range kept to it: {Offset()}"));
    }

    /// <summary>
    ///     With PVS on: something by the aim, out of the player's own view, is sent to them while the window is open, and
    ///         not before or after; something as far off but nowhere near the aim is never sent.
    /// </summary>
    [Test]
    public async Task WhatIsNearTheAimIsSentOnlyWhileTheWindowIsOpen()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);
        await SetUp(_mortar);

        var viewRange = 0f;
        var aim = Vector2.Zero;
        var control = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            viewRange = Server.CfgMan.GetCVar(CVars.NetMaxUpdateRange);
            var distance = MortarComponent.MaxRange;
            var mortarPosition = SEntMan.System<SharedTransformSystem>().GetWorldPosition(_mortarUid);
            aim = mortarPosition + new Vector2(distance, 0f);
            control = mortarPosition - new Vector2(distance, 0f);
        });
        Assert.That(aim.X - 1.5f, Is.GreaterThan(viewRange), "precondition: the aim is out of the player's own view");

        var before = await SpawnMarkers(aim, control);
        await Pair.RunTicksSync(20);
        await AssertArrived(before.Near, false, "before the window opens, nothing by the aim should be sent");

        await OpenWindow();
        await Client.WaitPost(() =>
        {
            var window = ClientWindow()!;
            window.BearingEdit.SetText("90", invokeEvent: true);
            window.DistanceEdit.SetText((aim.X - 0.5f).ToString(), invokeEvent: true);
        });
        await Pair.RunTicksSync(20);

        var open = await SpawnMarkers(aim, control);
        await Pair.RunTicksSync(20);
        await AssertArrived(open.Near, true, "while the window is open, what is by the aim should be sent");
        await AssertArrived(open.Far, false, "what is as far off but nowhere near the aim should not be sent");

        await Server.WaitPost(() => SEntMan.System<SharedUserInterfaceSystem>().CloseUi(_mortarUid, KsMortarUiKey.Key, _userUid));
        await Pair.RunTicksSync(10);

        var closed = await SpawnMarkers(aim, control);
        await Pair.RunTicksSync(20);
        await AssertArrived(closed.Near, false, "once the window is closed, nothing more by the aim should be sent");
    }

    /// <summary>
    ///     A marker a tile from the aim, and one as far from the mortar the other way.
    /// </summary>
    private async Task<(NetEntity Near, NetEntity Far)> SpawnMarkers(Vector2 aim, Vector2 control)
    {
        NetEntity near = default, far = default;
        await Server.WaitPost(() =>
        {
            near = SEntMan.GetNetEntity(SEntMan.SpawnEntity(_marker, new MapCoordinates(aim + new Vector2(0f, 1f), _map.MapId)));
            far = SEntMan.GetNetEntity(SEntMan.SpawnEntity(_marker, new MapCoordinates(control, _map.MapId)));
        });

        return (near, far);
    }

    private async Task AssertArrived(NetEntity netEntity, bool expected, string message)
    {
        await Client.WaitAssertion(() => Assert.That(CEntMan.TryGetEntity(netEntity, out _), Is.EqualTo(expected), message));
    }
}
