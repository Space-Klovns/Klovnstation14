#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.ShieldBash;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Jittering;
using Content.Shared.Timing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ShieldBash;

/// <summary>
///     Content test: which of the real weapons bang on which of the real shields. Shield shoves are in
///         <see cref="ComplexShove.KsShoveTest"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(KsShieldBashSystem))]
public sealed class KsShieldBashTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Shield = "RiotShield";
    private static readonly EntProtoId EnergyShield = "EnergyShield";

    private static readonly EntProtoId Stunbaton = "Stunbaton";
    private static readonly EntProtoId BaseBallBat = "BaseBallBat";
    private static readonly EntProtoId Crowbar = "Crowbar";
    private static readonly EntProtoId Katana = "Katana";
    private static readonly EntProtoId Paper = "Paper";

    /// <summary>
    ///     Anything with a melee attack bashes, tools included: the baseball bat (it rolls dough), the crowbar and
    ///         the katana (it slices) are all tools. Another shield does not, nor does something that can't hit.
    /// </summary>
    private static IEnumerable<TestCaseData> BashCases()
    {
        yield return new TestCaseData(Stunbaton, true);
        yield return new TestCaseData(BaseBallBat, true);
        yield return new TestCaseData(Crowbar, true);
        yield return new TestCaseData(Katana, true);
        yield return new TestCaseData(Shield, false);
        yield return new TestCaseData(Paper, false);
    }

    [TestCaseSource(nameof(BashCases))]
    public async Task WeaponBashesShield(EntProtoId weaponPrototype, bool bashes)
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity(Human, testMap.GridCoords);
            var shieldUid = SEntMan.SpawnEntity(Shield, testMap.GridCoords);
            var weaponUid = SEntMan.SpawnEntity(weaponPrototype, testMap.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(userUid, shieldUid));

            SEntMan.System<SharedInteractionSystem>().InteractUsing(userUid, weaponUid, shieldUid, testMap.GridCoords, checkCanInteract: false);

            Assert.That(SEntMan.System<UseDelaySystem>().IsDelayed(shieldUid, KsShieldBashSystem.BashDelayId), Is.EqualTo(bashes),
                bashes ? $"{weaponPrototype} must bash the shield." : $"{weaponPrototype} must not bash the shield.");
            Assert.That(SEntMan.HasComponent<JitteringComponent>(userUid), Is.EqualTo(bashes),
                "Bashing, and only bashing, must shake the basher.");
        });
    }

    /// <summary>
    ///     An energy shield only rings while it is on.
    /// </summary>
    [Test]
    public async Task EnergyShieldBashesOnlyWhileOn()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity(Human, testMap.GridCoords);
            var shieldUid = SEntMan.SpawnEntity(EnergyShield, testMap.GridCoords);
            var weaponUid = SEntMan.SpawnEntity(Stunbaton, testMap.GridCoords);
            var interactionSystem = SEntMan.System<SharedInteractionSystem>();
            var useDelaySystem = SEntMan.System<UseDelaySystem>();
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(userUid, shieldUid));

            interactionSystem.InteractUsing(userUid, weaponUid, shieldUid, testMap.GridCoords, checkCanInteract: false);
            Assert.That(useDelaySystem.IsDelayed(shieldUid, KsShieldBashSystem.BashDelayId), Is.False,
                "An energy shield that is off must not be bashed.");

            Assert.That(SEntMan.System<ItemToggleSystem>().TryActivate(shieldUid, userUid));
            interactionSystem.InteractUsing(userUid, weaponUid, shieldUid, testMap.GridCoords, checkCanInteract: false);
            Assert.That(useDelaySystem.IsDelayed(shieldUid, KsShieldBashSystem.BashDelayId), Is.True,
                "An energy shield that is on must be bashed.");
        });
    }
}
