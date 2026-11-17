#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Spawner;
using Robust.Shared.Audio.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.OreVent;

/// <summary>
///     An ore vent's telegraph says when its creature comes out, as tgstation's vents do.
/// </summary>
[TestFixture]
[TestOf(typeof(KsPopupOnDespawnSystem))]
public sealed class KsOreVentTelegraphTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const int TicksPerSecond = 30;

    private static readonly EntProtoId Telegraph = "KsOreVentTelegraphGoliath";
    private static readonly EntProtoId Observer = "MobObserver";
    private static readonly EntProtoId Spawner = "KsOreVentMobSpawnerLava";

    [Test]
    public async Task TelegraphPopsUpWhenItsCreatureEmerges()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitPost(() =>
        {
            var observerUid = SEntMan.SpawnEntity(Observer, map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, observerUid);
        });

        await RunUntilSynced();

        var expected = Client.ResolveDependency<ILocalizationManager>().GetString("ks-specific-orevent-creature-emerges");
        var popupSystem = CEntMan.System<Content.Client.Popups.PopupSystem>();

        await Server.WaitPost(() => SEntMan.SpawnEntity(Telegraph, map.GridCoords));
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            Assert.That(popupSystem.WorldLabels.Select(label => label.Text), Does.Not.Contain(expected),
                "the popup should wait for the creature, not come with the telegraph");
        });

        await Pair.RunTicksSync(TicksPerSecond * 2);

        await Client.WaitAssertion(() =>
        {
            Assert.That(popupSystem.WorldLabels.Select(label => label.Text), Does.Contain(expected),
                "the popup should show once the creature emerges");
        });
    }

    [Test]
    public async Task SpawnerDrillsOncePerWaveNotOncePerTelegraph()
    {
        var map = await Pair.CreateTestMap();

        int AudioCount()
        {
            var count = 0;
            var query = SEntMan.AllEntityQueryEnumerator<AudioComponent>();
            while (query.MoveNext(out _, out _))
                count++;

            return count;
        }

        // A telegraph on its own makes no noise.
        var baseline = 0;
        await Server.WaitPost(() =>
        {
            baseline = AudioCount();
            SEntMan.SpawnEntity(Telegraph, map.GridCoords);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(AudioCount(), Is.EqualTo(baseline), "the telegraph itself should be silent"));

        // The spawner fires on the tick after it is made.
        await Server.WaitPost(() => SEntMan.SpawnEntity(Spawner, map.GridCoords));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(AudioCount(), Is.EqualTo(baseline + 1), "one drilling sound per wave"));
    }
}
