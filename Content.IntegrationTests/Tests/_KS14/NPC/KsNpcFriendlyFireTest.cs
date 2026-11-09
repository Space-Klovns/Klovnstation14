#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Squad;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.FactionCollision;
using Content.Shared.Projectiles;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Every projectile hit, and who fired it.
/// </summary>
public sealed partial class KsFriendlyFireProbeSystem : EntitySystem
{
    public readonly List<(EntityUid Target, EntityUid? Shooter)> Hits = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnHit);
    }

    private void OnHit(Entity<ProjectileComponent> entity, ref ProjectileHitEvent args)
    {
        Hits.Add((args.Target, args.Shooter));
    }
}

/// <summary>
///     Live: real operatives, with shots that hit their own side (no <see cref="KsFactionCollisionShooterComponent"/>), in a
///         corridor a tile wide with a hostile at the end, so those at the back always have a squadmate in front. They hold
///         fire for them, and the one in front still shoots. With fire lanes off, the control: they shoot each other.
/// </summary>
[TestFixture]
public sealed class KsNpcFriendlyFireTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<Content.Server.NPC.HTN.HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    [TestCase(true)]
    [TestCase(false)]
    public async Task TestOperativesDoNotShootThroughEachOther(bool fireLanes)
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        await OverrideCVar(Side.Server, KsCCVars.NpcSquadFireLanes, fireLanes);

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var probe = entManager.System<KsFriendlyFireProbeSystem>();
        var mobs = new List<EntityUid>();
        EntityUid hostileUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-10, -3), new Vector2i(10, 3)).Owner;

            for (var x = -10; x <= 10; x++)
            {
                for (var y = -3; y <= 3; y++)
                {
                    if (y != 0 || x is < -8 or > 8)
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            for (var x = -5; x <= -3; x++)
            {
                SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, x, 0);
            }

            hostileUid = SpawnAt(entManager, "KsSquadTestMobNanoTrasen", gridUid, 5, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
        });

        await Pair.RunTicksSync(90);

        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID != map.MapId || uid == hostileUid)
                    continue;

                mobs.Add(uid);
                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);
                entManager.RemoveComponent<KsFactionCollisionShooterComponent>(uid);
            }

            probe.Hits.Clear();
        });

        Assert.That(mobs, Has.Count.EqualTo(3), "the test needs the three of them");

        await Pair.RunTicksSync(450);

        var friendlyHits = 0;
        var hostileHits = 0;
        await Server.WaitPost(() =>
        {
            friendlyHits = probe.Hits.Count(hit => mobs.Contains(hit.Target) && hit.Shooter is { } shooterUid && mobs.Contains(shooterUid));
            hostileHits = probe.Hits.Count(hit => hit.Target == hostileUid);

        });

        if (fireLanes)
        {
            Assert.That(friendlyHits, Is.Zero, "nobody should shoot a squadmate in its way");
            Assert.That(hostileHits, Is.GreaterThan(0), "but the hostile should still be shot: holding fire is not never firing");
        }
        else
        {
            Assert.That(friendlyHits, Is.GreaterThan(0), "control: without fire lanes, those behind shoot those in front");
        }
    }
}
