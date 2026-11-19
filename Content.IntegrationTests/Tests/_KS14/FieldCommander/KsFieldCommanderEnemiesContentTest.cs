#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Charges;
using Content.Server._KS14.NPC.Squad;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server._KS14.WeldedParts;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Humanoid.Systems;
using Content.Shared._KS14.NPC;
using Content.Shared.Atmos;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Content: the field commander's sniper, juggernaut and breachers (private module) come armed, the juggernaut cannot
///         be stunned, breachers shut out of a room blow the wall nearest the noise, and the tanks come with an armed turret
///         welded on.
/// </summary>
[TestFixture]
[TestOf(typeof(RandomHumanoidSystem))]
public sealed class KsFieldCommanderEnemiesContentTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The private module's units: run only where they are present.
    private const string Sniper = "KsFcEliteOperativeSynArmedSniper";
    private const string Juggernaut = "KsFcOperativeSynArmedJuggernaut";
    private const string Breacher = "KsFcEliteOperativeSynArmedBreacher";
    private const string Tank = "KsFcMobTank";
    private const string FlamerTank = "KsFcMobTankFlamer";

    private async Task<EntityUid?> SpawnUnit(string settings)
    {
        if (!SProtoMan.HasIndex<Content.Shared.Humanoid.Prototypes.RandomHumanoidSettingsPrototype>(settings))
            return null;

        var map = await Pair.CreateTestMap();
        EntityUid unitUid = default;
        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-3, -3), new Vector2i(3, 3)).Owner;
            unitUid = SEntMan.System<RandomHumanoidSystem>()
                .SpawnRandomHumanoid(settings, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)), "test");
        });

        await RunSeconds(1f);
        return unitUid;
    }

    [Test]
    public async Task UnitsComeArmed([Values(Sniper, Juggernaut, Breacher)] string settings)
    {
        if (await SpawnUnit(settings) is not { } unitUid)
        {
            Assert.Ignore("the unit is in the private module");
            return;
        }

        await Server.WaitAssertion(() =>
        {
            var heldUid = SEntMan.System<SharedHandsSystem>().GetActiveItem(unitUid);
            Assert.That(heldUid != null && SEntMan.HasComponent<GunComponent>(heldUid.Value), Is.True,
                $"{settings} should spawn with a gun in hand");
        });
    }

    [Test]
    public async Task JuggernautCannotBeStunned()
    {
        if (await SpawnUnit(Juggernaut) is not { } unitUid)
        {
            Assert.Ignore("the juggernaut is in the private module");
            return;
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedStunSystem>().TryAddStunDuration(unitUid, TimeSpan.FromSeconds(5)), Is.False,
                "the juggernaut should shrug off a stun");
        });
    }

    /// <summary>
    ///     Two breachers outside a sealed room hear a noise against the inside of its far wall: they carry what it takes to
    ///         blow their way in, and they blow the wall by the noise, a long walk round, not the one in front of them.
    /// </summary>
    [Test]
    public async Task BreachersBlowTheWallNearestTheNoise()
    {
        if (!SProtoMan.HasIndex<Content.Shared.Humanoid.Prototypes.RandomHumanoidSettingsPrototype>(Breacher))
        {
            Assert.Ignore("the breachers are in the private module");
            return;
        }

        var map = await Pair.CreateTestMap();
        var tacticsSystem = SEntMan.System<NpcSquadTacticsSystem>();
        EntityUid gridUid = default, firstUid = default, secondUid = default;

        await Server.WaitPost(() =>
        {
            tacticsSystem.UpdatesPaused = true;
            gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-14, -14), new Vector2i(14, 14)).Owner;

            // Air outside, so a hole in a wall lets nothing out it shouldn't: breachers mind that.
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(moles, Atmospherics.T20C));

            // And the same inside: the grid simulates no air of its own, so its tiles read the map's.
            SEntMan.RemoveComponent<Content.Shared.Atmos.Components.GridAtmosphereComponent>(gridUid);

            for (var x = -1; x <= 7; x++)
            {
                for (var y = -1; y <= 6; y++)
                {
                    if (x is > -1 and < 7 && y is > -1 and < 6)
                        continue;

                    SpawnAt(SEntMan, TestWall, gridUid, x, y);
                }
            }

            var humanoidSystem = SEntMan.System<RandomHumanoidSystem>();
            firstUid = humanoidSystem.SpawnRandomHumanoid(Breacher, new EntityCoordinates(gridUid, new Vector2(-2.5f, 3.5f)), "test");
            secondUid = humanoidSystem.SpawnRandomHumanoid(Breacher, new EntityCoordinates(gridUid, new Vector2(-2.5f, 2.5f)), "test");
        });

        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            var chargeSystem = SEntMan.System<NpcChargeSystem>();
            Assert.That(chargeSystem.TryGetCharge(firstUid, null, out _) && chargeSystem.TryGetCharge(secondUid, null, out _), Is.True,
                "each breacher should carry a charge it can set off");
            Assert.That(SEntMan.System<NpcSquadSystem>().TryGetSquad(firstUid, out var squadEntity), "the two should form a squad");

            // Back where the scene has them, whatever they did while they waited.
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            transformSystem.SetCoordinates(firstUid, new EntityCoordinates(gridUid, new Vector2(-2.5f, 3.5f)));
            transformSystem.SetCoordinates(secondUid, new EntityCoordinates(gridUid, new Vector2(-2.5f, 2.5f)));

            tacticsSystem.NoteDisturbance(firstUid, new EntityCoordinates(gridUid, new Vector2(6.5f, 2.5f)));

            NpcChargeSite? site = null;
            for (var i = 0; i < 10 && site == null; i++)
            {
                tacticsSystem.UpdateNow();
                if (tacticsSystem.GetHunt(squadEntity!.Value.Owner) is { Phase: NpcHuntPhase.Blast, Blast.Charge: { } charge })
                    site = charge.Pick.Site;
            }

            var hunt = tacticsSystem.GetHunt(squadEntity!.Value.Owner);
            Assert.That(site, Is.Not.Null,
                $"breachers shut out of the room should blow their way in: {hunt?.Phase}, shut out {hunt?.ShutOut}, tried {hunt?.BlastTried}, sealed in {hunt?.SealedIn}");
            Assert.That(site!.Value.Tile.X, Is.EqualTo(7), "the wall by the noise, not the one in front of them");
        });
    }

    [Test]
    public async Task TanksComeWithAnArmedTurretWeldedOn([Values(Tank, FlamerTank)] string tank)
    {
        if (!SProtoMan.HasIndex<EntityPrototype>(tank))
        {
            Assert.Ignore("the tanks are in the private module");
            return;
        }

        var map = await Pair.CreateTestMap();
        EntityUid hullUid = default;
        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-5, -5), new Vector2i(5, 5)).Owner;
            hullUid = SEntMan.SpawnEntity(tank, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));
        });

        await RunSeconds(1f);

        await Server.WaitAssertion(() =>
        {
            var parts = SEntMan.System<KsWeldedPartsSystem>().GetParts(hullUid);
            Assert.That(parts, Has.Count.EqualTo(1), $"{tank} should come with its turret");
            Assert.That(SEntMan.HasComponent<GunComponent>(parts[0]), Is.True, "the turret should carry the gun");
        });
    }
}
