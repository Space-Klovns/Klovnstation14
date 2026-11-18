#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Humanoid.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Content: the field commander's sniper and juggernaut (private module) come armed, and the juggernaut cannot be
///         stunned.
/// </summary>
[TestFixture]
[TestOf(typeof(RandomHumanoidSystem))]
public sealed class KsFieldCommanderEnemiesContentTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The private module's units: run only where they are present.
    private const string Sniper = "KsFcEliteOperativeSynArmedSniper";
    private const string Juggernaut = "KsFcOperativeSynArmedJuggernaut";

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
    public async Task UnitsComeArmed([Values(Sniper, Juggernaut)] string settings)
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
}
