#nullable enable
using System;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Medical;

/// <summary>
///     Asphyxiation in crit, where nobody breathes, is its own rate: suffocating in vacuum can be made deadly without
///         making every critical patient die faster too.
/// </summary>
[TestFixture]
[TestOf(typeof(RespiratorSystem))]
public sealed class KsRespiratorCritDamageTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestRespiratorBase
  abstract: true
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      1000: Dead
  - type: Damageable
  - type: Injurable

- type: entity
  parent: KsTestRespiratorBase
  id: KsTestRespiratorCritRate
  components:
  - type: Respirator
    damage:
      types:
        Asphyxiation: 4
    critDamage:
      types:
        Asphyxiation: 1
    damageRecovery:
      types:
        Asphyxiation: -1

- type: entity
  parent: KsTestRespiratorBase
  id: KsTestRespiratorNoCritRate
  components:
  - type: Respirator
    damage:
      types:
        Asphyxiation: 4
    damageRecovery:
      types:
        Asphyxiation: -1
";

    private readonly EntProtoId _critRatePrototype = "KsTestRespiratorCritRate";
    private readonly EntProtoId _noCritRatePrototype = "KsTestRespiratorNoCritRate";
    private static readonly ProtoId<DamageTypePrototype> AsphyxiationType = "Asphyxiation";
    private static readonly ProtoId<DamageTypePrototype> BluntType = "Blunt";

    /// <summary>
    ///     The asphyxiation one suffocating cycle deals the mob, alive or critical.
    /// </summary>
    private async Task<FixedPoint2> SuffocateOnce(EntProtoId prototype, bool critical)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        EntityUid mobUid = default;
        FixedPoint2 before = default;
        var ticks = 0;

        await server.WaitPost(() =>
        {
            mobUid = entityManager.SpawnAtPosition(prototype, testMap.GridCoords);
            if (critical)
            {
                var blunt = new DamageSpecifier(server.ProtoMan.Index(BluntType), 150);
                entityManager.System<DamageableSystem>().SetDamage(mobUid, blunt);
            }

            entityManager.System<RespiratorSystem>().UpdateSaturation(mobUid, -100f);
            before = AsphyxiationOf(mobUid);

            // Up to the first cycle and no further: the next is a whole update interval later.
            var timing = server.ResolveDependency<IGameTiming>();
            var untilCycle = entityManager.GetComponent<RespiratorComponent>(mobUid).NextUpdate - timing.CurTime;
            ticks = (int)Math.Ceiling(untilCycle.TotalSeconds * timing.TickRate) + 2;
        });

        await server.WaitRunTicks(ticks);

        FixedPoint2 dealt = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.System<MobStateSystem>().IsCritical(mobUid), Is.EqualTo(critical),
                "The mob isn't in the state under test, so this proves nothing.");
            dealt = AsphyxiationOf(mobUid) - before;
        });

        return dealt;
    }

    private FixedPoint2 AsphyxiationOf(EntityUid mobUid)
    {
        return Pair.Server.EntMan.System<DamageableSystem>().GetAllDamage(mobUid).DamageDict.GetValueOrDefault(AsphyxiationType);
    }

    [Test]
    public async Task CritUsesTheCritRate()
    {
        var dealt = await SuffocateOnce(_critRatePrototype, critical: true);

        Assert.That(dealt, Is.EqualTo(FixedPoint2.New(1)), "A critical mob didn't suffocate at its crit rate.");
    }

    [Test]
    public async Task SuffocatingAwakeUsesTheNormalRate()
    {
        var dealt = await SuffocateOnce(_critRatePrototype, critical: false);

        Assert.That(dealt, Is.EqualTo(FixedPoint2.New(4)), "A conscious mob didn't suffocate at its normal rate.");
    }

    [Test]
    public async Task CritWithNoCritRateUsesTheNormalRate()
    {
        var dealt = await SuffocateOnce(_noCritRatePrototype, critical: true);

        Assert.That(dealt, Is.EqualTo(FixedPoint2.New(4)), "A critical mob with no crit rate didn't fall back to its normal rate.");
    }
}
