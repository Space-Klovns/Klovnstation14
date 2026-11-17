#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._KS14.NPC.Squad;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.FactionCollision;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Every projectile hit, and who fired it; and every shot, with what the gun was doing when it fired, to tell after the
///         fact why a friend was hit.
/// </summary>
public sealed partial class KsFriendlyFireProbeSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public readonly List<(EntityUid Target, EntityUid? Shooter)> Hits = new();

    /// <summary>
    ///     Each friendly fire hit, with where the one hit was: see <see cref="Classify"/>.
    /// </summary>
    public readonly List<(EntityUid Target, EntityUid Shooter, Vector2 TargetPosition, TimeSpan At)> HitDetails = new();

    public readonly List<Shot> Shots = new();

    /// <summary>
    ///     A shot as it was fired. <paramref name="FollowUp"/>: a round of a burst after the first, fired by the gun on its own.
    /// </summary>
    public readonly record struct Shot(EntityUid User, Vector2 From, Vector2 To, TimeSpan At, Angle Cone, bool FollowUp, bool Clear, string Others = "");

    /// <summary>
    ///     Those to note, for each shot, where they were from the shooter, and whether a wall was in between.
    /// </summary>
    public readonly List<EntityUid> Watched = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnHit);
        SubscribeLocalEvent<GunShotEvent>(OnShot);
    }

    private void OnHit(Entity<ProjectileComponent> entity, ref ProjectileHitEvent args)
    {
        Hits.Add((args.Target, args.Shooter));

        if (args.Shooter is { } shooterUid)
            HitDetails.Add((args.Target, shooterUid, _transformSystem.GetWorldPosition(args.Target), _gameTiming.CurTime));
    }

    private void OnShot(ref GunShotEvent args)
    {
        if (!_gunSystem.TryGetGun(args.User, out var gunEntity))
            return;

        var now = _gameTiming.CurTime;
        var userUid = args.User;
        var previous = Shots.FindLast(shot => shot.User == userUid);
        var followUp = gunEntity.Comp.SelectedMode == SelectiveFire.Burst &&
            previous.User == userUid &&
            (now - previous.At).TotalSeconds <= 1.5 / gunEntity.Comp.BurstFireRate;

        // What the check as it goes off made of it: the server's, which the client does not have.
        var from = _transformSystem.ToMapCoordinates(args.FromCoordinates);
        var to = _transformSystem.ToMapCoordinates(args.ToCoordinates);
        var clear = !EntityManager.TrySystem<NpcSquadFireLaneSystem>(out var fireLaneSystem) ||
            fireLaneSystem.IsShotClear(args.User, from, to);

        var others = new StringBuilder();
        if (EntityManager.TrySystem<Content.Server._KS14.NPC.LineOfFire.NpcLineOfFireSystem>(out var lineOfFireSystem))
        {
            others.Append($" model cone ±{lineOfFireSystem.GetShotCone(gunEntity).HalfAngle * 180 / System.MathF.PI:0.0}°;");
            var aim = (to.Position - from.Position).ToAngle();
            foreach (var otherUid in Watched)
            {
                // Or left over from an earlier test.
                if (otherUid == args.User || TerminatingOrDeleted(otherUid))
                    continue;

                var position = _transformSystem.GetMapCoordinates(otherUid);
                var offset = position.Position - from.Position;
                var walled = lineOfFireSystem.TryGetFirstHit(from, position, lineOfFireSystem.GetShotMask(args.User), out var hit);
                others.Append($" [{offset.Length():0.0} at {Angle.ShortestDistance(aim, offset.ToAngle()).Degrees:0.0}°{(walled ? $" wall@{hit.Distance:0.0} {ToPrettyString(hit.HitEntity)}" : "")}]");
            }
        }

        Shots.Add(new Shot(args.User, from.Position, to.Position, now, gunEntity.Comp.CurrentAngle, followUp, clear, others.ToString()));
    }

    /// <summary>
    ///     Why each friendly hit happened, going by the shot of its shooter's that best explains it:
    ///     <list type="bullet">
    ///         <item>muzzle: the one hit stood within a tile of the shooter;</item>
    ///         <item>burst: a round of a burst after the first, which nothing checked;</item>
    ///         <item>past target: a miss that carried on past what was aimed at;</item>
    ///         <item>cone: off the line the old check covers, inside the gun's spread;</item>
    ///         <item>in line: on that line, so it moved there after the check, or it was never made.</item>
    ///     </list>
    /// </summary>
    public string Classify(out Dictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>();
        var log = new StringBuilder();

        foreach (var (targetUid, shooterUid, targetPosition, at) in HitDetails)
        {
            Shot? best = null;
            var bestOff = double.MaxValue;

            foreach (var shot in Shots)
            {
                // Only those that could have got there by then: a round flies about 40 tiles a second.
                if (shot.User != shooterUid || shot.At > at ||
                    (at - shot.At).TotalSeconds > (targetPosition - shot.From).Length() / 40f + 0.15f)
                    continue;

                var off = System.Math.Abs(Angle.ShortestDistance((shot.To - shot.From).ToAngle(), (targetPosition - shot.From).ToAngle()).Theta);
                if (off >= bestOff)
                    continue;

                bestOff = off;
                best = shot;
            }

            string kind;
            if (best is not { } match)
                kind = "unknown";
            else
            {
                var direction = match.To - match.From;
                var aimDistance = direction.Length();
                direction /= aimDistance;
                var offset = targetPosition - match.From;
                var along = Vector2.Dot(offset, direction);
                var lateral = System.MathF.Abs(offset.X * direction.Y - offset.Y * direction.X);

                kind = offset.Length() < 1f ? "muzzle"
                    : match.FollowUp ? "burst"
                    : along > aimDistance + 0.5f ? "past target"
                    : lateral > 0.45f ? "cone"
                    : "in line";

                log.AppendLine($"{kind}: friend {offset.Length():0.0} off, {along:0.0} along of {aimDistance:0.0}, {lateral:0.00} aside, " +
                    $"{bestOff * 180 / System.Math.PI:0.0}° off the aim, cone ±{System.Math.Abs(match.Cone.Degrees) / 2:0.0}°, " +
                    $"{(match.Clear ? "clear" : "held")} as fired;{match.Others}");
            }

            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        return log.ToString();
    }
}

/// <summary>
///     Live: real operatives, with shots that hit their own side (no <see cref="KsFactionCollisionShooterComponent"/>, as in
///         game), shooting at a hostile with squadmates about.
/// </summary>
[TestFixture]
public sealed class KsNpcFriendlyFireTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<Content.Server.NPC.HTN.HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    /// <summary>
    ///     A pistol's bullet, without end and dead straight.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsFriendlyFireTestGun
  components:
  - type: Item
  - type: Gun
    fireRate: 20
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BasicEntityAmmoProvider
    proto: BulletPistol
    capacity: 1000
    count: 1000
";

    /// <summary>
    ///     In a corridor a tile wide with a hostile at the end, so those at the back always have a squadmate in front. They
    ///         hold fire for them, and the one in front still shoots. With fire lanes off, the control: they shoot each other.
    /// </summary>
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
            mobs.AddRange(GetOperatives(entManager, map.MapId, hostileUid));
            probe.Watched.Clear();
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

    /// <summary>
    ///     A firefight in an open room: a squad against a hostile that will not go down, moving across their front. Some
    ///         squads put members on both sides of it. Nobody may hit a squadmate: not with the gun's spread, not with a miss
    ///         that carries on past the hostile, not with the rest of a burst. And they still fire.
    /// </summary>
    [TestCase("KsRandomHumanoidSpawnerOperativeRedacted", "KsRandomHumanoidSpawnerOperativeRedacted", "KsSquadTestMobSyndicate", false)]
    [TestCase("KsRandomHumanoidSpawnerOperativeRedacted", "KsRandomHumanoidSpawnerOperativeRedacted", "KsSquadTestMobSyndicate", true)]
    [TestCase("KsRandomHumanoidSpawnerOperativeNtArmedXL8", "KsRandomHumanoidSpawnerOperativeNtArmedPistol", "KsSquadTestMobSyndicate", true)]
    [TestCase("KsRandomHumanoidSpawnerOperativeSynArmedBulldog", "KsRandomHumanoidSpawnerOperativeSynArmedPistol", "KsSquadTestMobNanoTrasen", true)]
    public async Task TestNoFriendlyFireInAnOpenFight(string leaderSpawner, string memberSpawner, string hostile, bool crossfire)
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var map = await Pair.CreateTestMap();
        var probe = entManager.System<KsFriendlyFireProbeSystem>();
        var mobs = new List<EntityUid>();
        EntityUid gridUid = default, hostileUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-13, -8), new Vector2i(13, 8)).Owner;
            GiveGravity(entManager, gridUid);

            for (var x = -13; x <= 13; x++)
            {
                for (var y = -8; y <= 8; y++)
                {
                    if (x is -13 or 13 || y is -8 or 8)
                        SpawnAt(entManager, "WallSolid", gridUid, x, y);
                }
            }

            for (var i = 0; i < 4; i++)
            {
                SpawnAt(entManager, i == 0 ? leaderSpawner : memberSpawner, gridUid, -10, i - 2);
            }

            // Out of sight until the fight starts.
            hostileUid = SpawnAt(entManager, hostile, gridUid, 0, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(40f, 40f)));
        });

        await Pair.RunTicksSync(240); // for a squad to form

        await Server.WaitPost(() =>
        {
            mobs.AddRange(GetOperatives(entManager, map.MapId, hostileUid));

            // Two of them round the far side: whatever misses one pair flies on at the other.
            if (crossfire)
            {
                for (var i = 2; i < mobs.Count; i++)
                {
                    transformSystem.SetCoordinates(mobs[i], new EntityCoordinates(gridUid, new Vector2(10.5f, i - 2.5f)));
                }
            }

            transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));
            probe.Watched.Clear();
            probe.Watched.AddRange(mobs);
            probe.Hits.Clear();
            probe.HitDetails.Clear();
            probe.Shots.Clear();
        });

        Assert.That(mobs, Has.Count.EqualTo(4), "the test needs the four of them");

        // Across their front and back, a step at a time, as one walking.
        for (var tick = 0; tick < 600; tick += 5)
        {
            await Pair.RunTicksSync(5);
            var y = 0.5f + 4f * System.MathF.Sin(tick / 90f);
            await Server.WaitPost(() => transformSystem.SetCoordinates(hostileUid, new EntityCoordinates(gridUid, new Vector2(0.5f, y))));
        }

        var friendlyHits = 0;
        var hostileHits = 0;
        var shots = 0;
        var classified = string.Empty;
        Dictionary<string, int> counts = new();
        await Server.WaitPost(() =>
        {
            friendlyHits = probe.HitDetails.Count(hit => mobs.Contains(hit.Target) && mobs.Contains(hit.Shooter));
            hostileHits = probe.Hits.Count(hit => hit.Target == hostileUid);
            shots = probe.Shots.Count(shot => mobs.Contains(shot.User));
            probe.HitDetails.RemoveAll(hit => !mobs.Contains(hit.Target) || !mobs.Contains(hit.Shooter));
            classified = probe.Classify(out counts);
        });

        TestContext.Out.WriteLine($"{shots} shots, {hostileHits} on the hostile, {friendlyHits} on squadmates: " +
            string.Join(", ", counts.Select(pair => $"{pair.Value} {pair.Key}")));
        TestContext.Out.WriteLine(classified);

        Assert.That(friendlyHits, Is.Zero, "nobody should hit a squadmate");
        Assert.That(hostileHits, Is.GreaterThan(0), "but the hostile should still be shot: holding fire is not never firing");
    }

    /// <summary>
    ///     Where round a shooter a friend is hit by a round fired east from it, by distance and bearing, against where
    ///         <see cref="NpcSquadFireLaneSystem.IsShotClear"/> has it: every friend hit was held for, and a friend just beside,
    ///         or a little way behind, was not. This is what <see cref="NpcSquadFireLaneSystem.ShotBackReach"/> was measured by.
    /// </summary>
    [Test]
    public async Task TestMapWhereAShotStarts()
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative spawners are in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var gunSystem = entManager.System<SharedGunSystem>();
        var fireLaneSystem = entManager.System<NpcSquadFireLaneSystem>();
        var wrong = new List<string>();
        var map = await Pair.CreateTestMap();
        var probe = entManager.System<KsFriendlyFireProbeSystem>();
        EntityUid gridUid = default, shooterUid = default, friendUid = default, gunUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -6), new Vector2i(6, 6)).Owner;
            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, 0, 0);
            friendUid = SpawnAt(entManager, "KsSquadTestMobSyndicate", gridUid, 3, 3);
            gunUid = SpawnAt(entManager, "KsFriendlyFireTestGun", gridUid, -3, -3);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(friendUid);
        });

        await Pair.RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            shooterUid = GetOperatives(entManager, map.MapId, friendUid).Single();
            // Measured with nothing held back: no NPC, so no check as the round goes off.
            entManager.RemoveComponent<Content.Server.NPC.HTN.HTNComponent>(shooterUid);
            entManager.RemoveComponent<Content.Shared.NPC.ActiveNPCComponent>(shooterUid);
        });

        var log = new StringBuilder("bearing:");
        for (var bearing = 0; bearing < 360; bearing += 30)
        {
            log.Append($"{bearing,5}");
        }

        log.AppendLine();
        var origin = new Vector2(0.5f, 0.5f);

        foreach (var distance in new[] { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.2f })
        {
            log.Append($"{distance,7:0.0}:");

            for (var bearing = 0; bearing < 360; bearing += 30)
            {
                var hit = false;
                var clear = false;
                var direction = Angle.FromDegrees(bearing).ToVec();

                await Server.WaitPost(() =>
                {
                    transformSystem.SetCoordinates(shooterUid, new EntityCoordinates(gridUid, origin));
                    transformSystem.SetCoordinates(friendUid, new EntityCoordinates(gridUid, origin + direction * distance));
                    // Still: the friend is pushed off the shooter after each overlap, and the check counts where it is going.
                    var physicsSystem = entManager.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
                    physicsSystem.SetLinearVelocity(shooterUid, Vector2.Zero);
                    physicsSystem.SetLinearVelocity(friendUid, Vector2.Zero);
                    probe.Hits.Clear();

                    clear = fireLaneSystem.IsShotClear(shooterUid,
                        new MapCoordinates(origin, map.MapId),
                        new MapCoordinates(origin + new Vector2(5f, 0f), map.MapId));

                    probe.Shots.Clear();
                    gunSystem.AttemptShoot(shooterUid, (gunUid, entManager.GetComponent<GunComponent>(gunUid)), new EntityCoordinates(gridUid, origin + new Vector2(5f, 0f)));
                });

                await Pair.RunTicksSync(3);
                var fired = false;
                await Server.WaitPost(() =>
                {
                    hit = probe.Hits.Any(h => h.Target == friendUid);
                    fired = probe.Shots.Count > 0;
                });
                log.Append(!fired ? "    ?" : hit ? "    X" : "    .");
                log.Append(clear ? ' ' : '*');

                // Just beside it, or a little way behind: never hit, and should not be held for.
                var safe = distance >= 0.5f && bearing is 90 or 270 || distance >= 0.7f && bearing is >= 150 and <= 210;

                if (hit && clear)
                    wrong.Add($"hit at {distance} tiles, {bearing} degrees, and not held for");
                else if (!hit && safe && !clear)
                    wrong.Add($"held for at {distance} tiles, {bearing} degrees, where a round never goes");
                await Pair.RunTicksSync(2);
            }

            log.AppendLine();
        }

        TestContext.Out.WriteLine(log.ToString());
        Assert.That(wrong, Is.Empty, "X: hit; *: held for");
    }

    /// <summary>
    ///     The operatives on the test map, all but the hostile, made unkillable and with shots that hit their own side.
    /// </summary>
    private static List<EntityUid> GetOperatives(IEntityManager entManager, MapId mapId, EntityUid hostileUid)
    {
        var operatives = new List<EntityUid>();
        var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();

        while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
        {
            if (transformComponent.MapID != mapId || uid == hostileUid)
                continue;

            operatives.Add(uid);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);
            entManager.RemoveComponent<KsFactionCollisionShooterComponent>(uid);
        }

        return operatives;
    }
}
