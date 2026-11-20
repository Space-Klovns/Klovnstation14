#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.WeldedParts;
using Content.Shared._KS14.LockOn;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Behaviour: a Field Commander tank's turret takes on the defenders' turrets and mortars, which have no mob state, as it
///         does the defenders themselves, and leaves alone a mortar its own side crews, which has its crewman's faction.
/// </summary>
[TestFixture]
public sealed class KsFieldCommanderTankTargetsTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The private module's tank: run only where it is present.
    private readonly EntProtoId _tank = "KsFcMobTank";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _defenderTurret = "KsTestFcTankTargetDefenderTurret";
    private readonly EntProtoId _defenderMortar = "KsTestFcTankTargetDefenderMortar";
    private readonly EntProtoId _ownMortar = "KsTestFcTankTargetOwnMortar";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseWeaponTurret
  id: KsTestFcTankTargetDefenderTurret
  components:
  - type: NpcFactionMember
    factions:
    - NanoTrasen
  - type: HTN # one that does not shoot back
    rootTask:
      task: IdleCompound

# As an abandoned mortar is: on the side its crew was hostile to.
- type: entity
  parent: KsMortar
  id: KsTestFcTankTargetDefenderMortar
  components:
  - type: NpcFactionMember
    factions:
    - NanoTrasen

# As a crewed one is: on its crewman's side, the tank's own.
- type: entity
  parent: KsMortar
  id: KsTestFcTankTargetOwnMortar
  components:
  - type: NpcFactionMember
    factions:
    - Syndicate
";

    /// <summary>
    ///     With only a defender turret or a defender mortar in reach, the tank's turret locks on to it; with its own side's
    ///         mortar nearer than a defender turret, it locks on to the turret and never on to the mortar.
    /// </summary>
    [TestCase("turret", false)]
    [TestCase("mortar", false)]
    [TestCase("turret", true)]
    public async Task TankTurretTakesOnDefenderStructures(string target, bool ownMortarNearer)
    {
        if (!SProtoMan.HasIndex(_tank))
            Assert.Ignore("the Field Commander tanks are in the private _KsModule submodule, which is not present");

        var map = await Pair.CreateTestMap();
        var tickRate = (float)Server.ResolveDependency<IGameTiming>().TickRate;
        EntityUid turretPartUid = default, targetUid = default;
        EntityUid? ownMortarUid = null;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, Server.ResolveDependency<ITileDefinitionManager>(), map.MapId, map.Grid,
                new Vector2i(-10, -10), new Vector2i(10, 10)).Owner;

            var tankUid = SpawnAt(SEntMan, _tank, gridUid, 0, 0);
            var parts = SEntMan.System<KsWeldedPartsSystem>().GetParts(tankUid);
            Assert.That(parts, Has.Count.EqualTo(1), "precondition: the tank has its turret");
            turretPartUid = parts[0];

            targetUid = SpawnAt(SEntMan, target == "turret" ? _defenderTurret : _defenderMortar, gridUid, 0, 6);
            SEntMan.System<SharedGodmodeSystem>().EnableGodmode(targetUid);

            if (ownMortarNearer)
            {
                ownMortarUid = SpawnAt(SEntMan, _ownMortar, gridUid, 3, 0);
                SEntMan.System<SharedGodmodeSystem>().EnableGodmode(ownMortarUid.Value);
            }
        });

        // However long the turret takes to plan and come round, up to a generous cap.
        var lockedOn = new List<EntityUid>();
        for (var tick = 0; tick < (int)(15f * tickRate) && !lockedOn.Contains(targetUid); tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                if (SEntMan.GetComponent<KsTargetLockOnComponent>(turretPartUid).Target is { } lockTarget && !lockedOn.Contains(lockTarget))
                    lockedOn.Add(lockTarget);
            });
        }

        Assert.That(lockedOn, Does.Contain(targetUid), $"the tank's turret should have locked on to the defender {target}");
        if (ownMortarUid is { } ownUid)
            Assert.That(lockedOn, Does.Not.Contain(ownUid), "the tank's turret should never lock on to its own side's mortar");
    }
}
