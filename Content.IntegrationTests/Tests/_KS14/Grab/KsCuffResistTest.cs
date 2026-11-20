#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.Resist;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.DoAfter;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     Resisting while cuffed starts breaking out of the cuffs. With nothing to resist, nothing takes the attempt.
/// </summary>
[TestFixture]
[TestOf(typeof(KsCuffResistSystem))]
public sealed class KsCuffResistTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Handcuffs = "Handcuffs";

    [TestCase(true)]
    [TestCase(false)]
    public async Task ResistingBreaksOutOfCuffs(bool cuffed)
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid playerUid = default;

        await Server.WaitAssertion(() =>
        {
            playerUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            if (!cuffed)
                return;

            var cuffsUid = SEntMan.SpawnEntity(Handcuffs, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            Assert.That(SEntMan.System<SharedCuffableSystem>().TryAddNewCuffs(playerUid, playerUid, cuffsUid));
        });

        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CuffableComponent>(playerUid).CuffedHandCount > 0, Is.EqualTo(cuffed));

            var taken = SEntMan.System<KsResistSystem>().TryResist(playerUid);
            var breakingOut = SEntMan.TryGetComponent<DoAfterComponent>(playerUid, out var doAfters) &&
                              doAfters.DoAfters.Values.Any(doAfter => !doAfter.Cancelled && !doAfter.Completed);

            Assert.That(taken, Is.EqualTo(cuffed),
                cuffed ? "Resisting while cuffed must be taken by the cuffs." : "With nothing to resist, nothing takes the attempt.");
            Assert.That(breakingOut, Is.EqualTo(cuffed),
                cuffed ? "Resisting while cuffed must start breaking out of the cuffs." : "Nothing must start.");
        });
    }
}
