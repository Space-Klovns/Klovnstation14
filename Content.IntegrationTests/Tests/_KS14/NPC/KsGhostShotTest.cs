#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
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
///     Notes shots on the server and muzzle flashes arriving on the client, with the tick each happened on.
/// </summary>
public sealed partial class KsFlashProbeSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    public readonly List<uint> Shots = new();
    public readonly List<uint> Flashes = new();

    /// <summary>
    ///     Flashes with no tick to say when they were fired, which the client cannot tell are late.
    /// </summary>
    public int Unstamped;
    public readonly List<(uint Tick, string Prototype)> Effects = new();
    public readonly List<uint> Bullets = new();

    public override void Shutdown()
    {
        base.Shutdown();
        EntityManager.EntityInitialized -= OnEntityInitialized;
    }

    private void OnEntityInitialized(Entity<MetaDataComponent> entity)
    {
        if (entity.Comp.EntityPrototype?.ID is { } id && id.Contains("Muzzle"))
            Effects.Add((_gameTiming.CurTick.Value, id));

        if (HasComp<Content.Shared.Projectiles.ProjectileComponent>(entity))
            Bullets.Add(_gameTiming.CurTick.Value);
    }

    public override void Initialize()
    {
        base.Initialize();
        EntityManager.EntityInitialized += OnEntityInitialized;
        SubscribeAllEvent<MuzzleFlashEvent>(OnFlash);
        SubscribeLocalEvent<GunShotEvent>(OnShot);
    }

    private void OnFlash(MuzzleFlashEvent args)
    {
        Flashes.Add(_gameTiming.CurTick.Value);

        if (args.FiredAt == GameTick.Zero)
            Unstamped++;
    }

    private void OnShot(ref GunShotEvent args)
    {
        Shots.Add(_gameTiming.CurTick.Value);
    }

}

/// <summary>
///     A player watching NPCs have a gunfight. What the server fired and what the client was told to show should be the same
///         shots, at the same moments.
/// </summary>
public sealed class KsGhostShotTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    [Test]
    public async Task TestEveryFlashIsAShot()
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        var serverProbe = entManager.System<KsFlashProbeSystem>();
        var clientProbe = Client.EntMan.System<KsFlashProbeSystem>();
        EntityUid gridUid = default, hostileUid = default;

        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-12, -12), new Vector2i(12, 12)).Owner;

            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, -2, 0);
            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, -2, 2);

            hostileUid = SpawnAt(entManager, "KsSquadTestMobNanoTrasen", gridUid, 6, 0);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);

            var watcherUid = SpawnAt(entManager, "MobHuman", gridUid, 0, 6);
            entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(watcherUid);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, watcherUid);
        });

        await Pair.RunTicksSync(90);

        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID == map.MapId)
                    entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);
            }
        });

        await Client.WaitPost(() =>
        {
            clientProbe.Flashes.Clear();
            clientProbe.Effects.Clear();
            clientProbe.Bullets.Clear();
        });
        await Server.WaitPost(() => serverProbe.Shots.Clear());

        await Pair.RunTicksSync(450);

        // The fight ends: nothing left to shoot at.
        await Server.WaitPost(() => entManager.DeleteEntity(hostileUid));
        uint fightEnded = 0;
        await Server.WaitPost(() => fightEnded = Server.Timing.CurTick.Value);

        await Pair.RunTicksSync(450);

        var shots = new List<uint>();
        var flashes = new List<uint>();
        var effects = new List<(uint Tick, string Prototype)>();
        var bullets = 0;
        await Server.WaitPost(() => shots.AddRange(serverProbe.Shots));
        await Client.WaitPost(() =>
        {
            flashes.AddRange(clientProbe.Flashes);
            effects.AddRange(clientProbe.Effects);
            bullets = clientProbe.Bullets.Count;
        });

        Assert.That(shots, Is.Not.Empty, "the test needs a gunfight");

        // A flash that turns up long after its shot is not shown; one on time is.
        var effectsBefore = 0;
        var lateEffects = 0;
        var freshEffects = 0;
        await Client.WaitPost(() => effectsBefore = clientProbe.Effects.Count);

        await Client.WaitPost(() =>
        {
            var gunUid = Client.EntMan.EntityQueryEnumerator<Content.Shared.Weapons.Ranged.Components.GunComponent>();
            Assert.That(gunUid.MoveNext(out var clientGunUid, out _), "the test needs a gun on the client");

            var lastRealTick = ((Robust.Client.Timing.IClientGameTiming) Client.Timing).LastRealTick;
            var late = new MuzzleFlashEvent(Client.EntMan.GetNetEntity(clientGunUid), "MuzzleFlashEffect", null, Angle.Zero) { FiredAt = new GameTick(lastRealTick.Value - 300) };
            Client.EntMan.EventBus.RaiseEvent(EventSource.Local, late);
            lateEffects = clientProbe.Effects.Count - effectsBefore;

            var fresh = new MuzzleFlashEvent(Client.EntMan.GetNetEntity(clientGunUid), "MuzzleFlashEffect", null, Angle.Zero) { FiredAt = lastRealTick };
            Client.EntMan.EventBus.RaiseEvent(EventSource.Local, fresh);
            freshEffects = clientProbe.Effects.Count - effectsBefore - lateEffects;
        });

        Assert.Multiple(() =>
        {
            Assert.That(clientProbe.Unstamped, Is.Zero, "every flash from the server should say when it was fired");
            Assert.That(lateEffects, Is.Zero, "a flash ten seconds late is not shown");
            Assert.That(freshEffects, Is.EqualTo(1), "one on time is");
            Assert.That(flashes.Count(tick => tick > fightEnded + 30), Is.Zero, "no flashes once the shooting has stopped");
            Assert.That(effects.Count(effect => effect.Tick > fightEnded + 30), Is.Zero, "and no effects from anywhere else either");
            Assert.That(shots.Count(tick => tick > fightEnded), Is.Zero, "the test needs the shooting to have stopped");

            // A shot or two either side of when the counting started. Past that, something is being shown that was never fired.
            Assert.That(System.Math.Abs(flashes.Count - shots.Count), Is.LessThanOrEqualTo(2), "one flash a shot");
            Assert.That(System.Math.Abs(bullets - shots.Count), Is.LessThanOrEqualTo(2), "and one bullet");
        });
    }
}
