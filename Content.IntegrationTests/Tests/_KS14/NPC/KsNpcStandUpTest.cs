#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.PlayDead;
using Content.Server._KS14.NPC.Squad;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Live: a knocked down operative gets back up once nothing keeps it down (<c>NpcStandUpSystem</c>). The game gets a
///         mob up by itself only while its knockdown allows it (<c>KnockedDownComponent.AutoStand</c>); a knockdown
///         without it (lava, some triggers, or a try at standing with no room) needs the stand-up key pressed, which an
///         NPC never did.
/// </summary>
[TestFixture]
public sealed class KsNpcStandUpTest : GameTest
{
    // Connected: a real NPC only runs while a player is about.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<Content.Server.NPC.HTN.HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";


    [TestPrototypes]
    private const string Prototypes = @"
- type: npcMeter
  id: KsStandUpTestCaution
  max: 100
  decayPerSecond: 0

# Its NpcPlayDead, put on a live operative: plays dead for sure once rattled.
- type: entity
  id: KsStandUpTestPlayDead
  components:
  - type: NpcPlayDead
    meter: KsStandUpTestCaution
    threshold: 50
    chance: 1
";

    /// <summary>
    ///     How long the knockdown lasts. Getting up takes the crawler's stand-up time on top (2s).
    /// </summary>
    private static readonly System.TimeSpan KnockdownTime = System.TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Ticks, at 30 a second, it has to be up within: the knockdown, the stand-up do-after, and a few seconds more.
    /// </summary>
    private const int UpWithinTicks = 300;

    /// <summary>
    ///     Ticks into the knockdown at which it has to be still down: getting up is for once it is over, not before.
    /// </summary>
    private const int StillDownAtTicks = 45;

    /// <summary>
    ///     <paramref name="autoStand"/>: whether the knockdown lets the game get it up by itself. Either way it gets up, and
    ///         not before the knockdown is over. False is the case that left operatives on the floor for good.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task TestKnockedDownOperativeGetsUp(bool autoStand)
    {
        var operativeUid = await SetUp();
        var entManager = Server.EntMan;
        var standingSystem = entManager.System<StandingStateSystem>();

        await Server.WaitPost(() =>
        {
            Assert.That(entManager.System<SharedStunSystem>().TryKnockdown(operativeUid, KnockdownTime, autoStand: autoStand, drop: false, force: true),
                "the test needs it knocked down");
        });

        var downAtStart = false;
        var stoodEarly = false;
        var upAt = -1;

        for (var tick = 0; tick < UpWithinTicks && upAt < 0; tick += 5)
        {
            await Pair.RunTicksSync(5);
            await Server.WaitPost(() =>
            {
                var down = standingSystem.IsDown(operativeUid) || entManager.HasComponent<KnockedDownComponent>(operativeUid);
                downAtStart |= tick == 0 && down;
                stoodEarly |= tick < StillDownAtTicks && !down;

                if (!down)
                    upAt = tick + 5;
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(downAtStart, "the test needs it on the floor to begin with");
            Assert.That(stoodEarly, Is.False, "it should stay down while the knockdown lasts");
            Assert.That(upAt, Is.GreaterThan(0), $"it should be up within {UpWithinTicks / 30}s, with nothing keeping it down");
        });
    }

    /// <summary>
    ///     Playing dead is lying there on purpose: a knockdown that ends meanwhile does not get it up.
    /// </summary>
    [Test]
    public async Task TestPlayingDeadStaysDown()
    {
        var operativeUid = await SetUp();
        var entManager = Server.EntMan;
        var standingSystem = entManager.System<StandingStateSystem>();
        var playDeadSystem = entManager.System<NpcPlayDeadSystem>();
        // A local, not a field: the YAML linter checks ids in fields against prototypes it loads, and not test ones.
        EntProtoId playDeadComponents = "KsStandUpTestPlayDead";

        await Server.WaitPost(() =>
        {
            entManager.AddComponents(operativeUid, Server.ProtoMan.Index(playDeadComponents), removeExisting: true);
            entManager.System<NpcMeterSystem>().Set(operativeUid, "KsStandUpTestCaution", 100f);
            playDeadSystem.UpdateNow(operativeUid);
            Assert.That(playDeadSystem.IsPlayingDead(operativeUid), "the test needs it playing dead");

            Assert.That(entManager.System<SharedStunSystem>().TryKnockdown(operativeUid, KnockdownTime, autoStand: false, drop: false, force: true),
                "the test needs it knocked down");
        });

        var stoodUp = false;

        for (var tick = 0; tick < UpWithinTicks && !stoodUp; tick += 5)
        {
            await Pair.RunTicksSync(5);
            await Server.WaitPost(() =>
            {
                stoodUp |= !standingSystem.IsDown(operativeUid);
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(playDeadSystem.IsPlayingDead(operativeUid), "the test needs it still playing dead at the end");
            Assert.That(stoodUp, Is.False, "it should stay down while it plays dead");
        });
    }

    /// <summary>
    ///     A live Syndicate operative on its own, on a floor with gravity, nobody about, and godmode on.
    /// </summary>
    private async Task<EntityUid> SetUp()
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid operativeUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-8, -8), new Vector2i(8, 8)).Owner;
            GiveGravity(entManager, gridUid);
            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, 0, 0);
        });

        await Pair.RunTicksSync(60); // the spawner makes the operative

        await Server.WaitPost(() =>
        {
            // The spawner's mob, not the spawner.
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID == map.MapId)
                    operativeUid = uid;
            }

            Assert.That(operativeUid, Is.Not.EqualTo(EntityUid.Invalid), "the test needs the operative");
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(operativeUid);
        });

        return operativeUid;
    }
}
