#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Squad;
using Content.Server.NPC.HTN;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Live, through real operatives: shot at, a regular one is pinned down (<c>KsSuppressed</c> rises) and goes to cover,
///         while an elite, shot at just the same, is not and does not. The pieces are
///         <see cref="KsNpcPinnedDownTest"/> (the sense layer) and <c>KsOperativeHtnRootTest.TestPinnedDownOperativeGoesToCover</c>
///         (the plan).
/// </summary>
[TestFixture]
public sealed class KsOperativePinnedDownLiveTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private readonly ProtoId<HTNCompoundPrototype> _operativeRoot = "KsOperativeCombatCompound";

    [Test]
    public async Task TestShotAtARegularOperativeIsPinnedDownAndAnEliteIsNot()
    {
        if (!Server.ProtoMan.HasIndex(_operativeRoot))
            Assert.Ignore("the operative HTN is in the private _KsModule submodule, which is not present");

        var entManager = Server.EntMan;
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var transformSystem = entManager.System<SharedTransformSystem>();
        var meterSystem = entManager.System<NpcMeterSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid gridUid = default;
        var hostiles = new List<EntityUid>();

        // Two rows, ten tiles apart: a hostile in the corner of each, a short wall, and an operative below the wall's end, in
        //      plain sight of its hostile with a corner a step away.
        await Server.WaitPost(() =>
        {
            gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-6, -16), new Vector2i(14, 8)).Owner;
            GiveGravity(entManager, gridUid);

            foreach (var rowY in new[] { 0, -10 })
            {
                for (var y = 2; y <= 4; y++)
                    SpawnAt(entManager, "WallSolid", gridUid, 2, rowY + y);

                var hostileUid = SpawnAt(entManager, NanoTrasenMob, gridUid, 0, rowY);
                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(hostileUid);
                hostiles.Add(hostileUid);
            }

            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedPistol", gridUid, 8, 1);
            SpawnAt(entManager, "KsRandomHumanoidSpawnerOperativeSynArmedBulldog", gridUid, 8, -9);
        });

        await Pair.RunTicksSync(120);

        EntityUid regularUid = default, eliteUid = default;
        await Server.WaitPost(() =>
        {
            var enumerator = entManager.EntityQueryEnumerator<NpcSquadMemberComponent, TransformComponent>();
            while (enumerator.MoveNext(out var uid, out _, out var transformComponent))
            {
                if (transformComponent.MapID != map.MapId || hostiles.Contains(uid))
                    continue;

                entManager.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(uid);

                // Each below the end of its row's wall, a step from the corner.
                var position = transformSystem.GetWorldPosition(uid);
                if (position.Y > -5f)
                {
                    regularUid = uid;
                    transformSystem.SetCoordinates(uid, new EntityCoordinates(gridUid, new Vector2(4.5f, 1.5f)));
                }
                else
                {
                    eliteUid = uid;
                    transformSystem.SetCoordinates(uid, new EntityCoordinates(gridUid, new Vector2(4.5f, -8.5f)));
                }
            }
        });

        Assert.That(regularUid, Is.Not.EqualTo(EntityUid.Invalid), "the regular operative should have spawned");
        Assert.That(eliteUid, Is.Not.EqualTo(EntityUid.Invalid), "and the elite");

        var pinned = false;
        var wentToCover = false;
        var eliteCover = false;
        var elitePeak = 0f;
        var regularPeak = 0f;

        // Each hostile fires at its row's operative, a round every few ticks, for a few seconds.
        for (var tick = 0; tick < 150; tick += 4)
        {
            await Pair.RunTicksSync(4);
            await Server.WaitPost(() =>
            {
                for (var i = 0; i < hostiles.Count; i++)
                {
                    var rowY = i == 0 ? 0f : -10f;
                    var target = i == 0 ? regularUid : eliteUid;
                    var ev = new GunShotEvent(hostiles[i],
                        new List<(EntityUid?, IShootable)>(),
                        new EntityCoordinates(gridUid, new Vector2(0.5f, rowY + 0.5f)),
                        transformSystem.GetMoverCoordinates(target));
                    entManager.EventBus.RaiseEvent(EventSource.Local, ref ev);
                }

                regularPeak = System.Math.Max(regularPeak, meterSystem.GetValue(regularUid, "KsSuppressed"));
                elitePeak = System.Math.Max(elitePeak, meterSystem.GetValue(eliteUid, "KsSuppressed"));
                pinned = regularPeak >= 60f;
                wentToCover |= entManager.GetComponent<HTNComponent>(regularUid).Blackboard.ContainsKey("PinnedCoverDueAt");
                eliteCover |= entManager.GetComponent<HTNComponent>(eliteUid).Blackboard.ContainsKey("PinnedCoverDueAt");
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(pinned, $"under fire the regular operative's meter should pass 60, peaked at {regularPeak}");
            Assert.That(wentToCover, "and it should have gone to cover (the pinned branch ran)");
            Assert.That(elitePeak, Is.Zero, "an elite shrugs it off");
            Assert.That(eliteCover, Is.False, "and goes nowhere for it");
        });
    }
}
