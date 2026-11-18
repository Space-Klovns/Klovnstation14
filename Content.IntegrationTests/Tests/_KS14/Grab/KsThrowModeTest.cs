#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.ThrowMode;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Throwing;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     Throw mode, from a real client: the HUD button toggles it, and every click in throw mode throws the active hand's
///         item at the click until it is toggled off. Out of throw mode the same request does nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(KsThrowModeSystem))]
public sealed class KsThrowModeTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task ClicksThrowUntilToggledOff()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid playerUid = default;
        EntityUid itemUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 8; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            playerUid = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, playerUid);
            itemUid = SEntMan.SpawnEntity("Wrench", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(playerUid, itemUid));
        });

        await Pair.RunTicksSync(10);

        var throwModeSystem = SEntMan.System<KsThrowModeSystem>();
        var target = new EntityCoordinates(testMap.Grid, 5.5f, 0.5f);

        // Out of throw mode a throw request is ignored.
        await RequestThrow(target);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(playerUid, itemUid), "Out of throw mode nothing is thrown."));

        // What the HUD's throw button sends.
        await Client.WaitPost(() =>
            Client.ResolveDependency<IClientEntityManager>().RaisePredictiveEvent(new KsToggleThrowModeEvent()));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(throwModeSystem.IsEnabled(playerUid), "The throw button must turn throw mode on."));

        await RequestThrow(target);

        EntityUid secondUid = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(playerUid, itemUid), Is.False, "A click in throw mode must throw.");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(itemUid), "The item must be thrown.");
            Assert.That(throwModeSystem.IsEnabled(playerUid), "Throw mode must stay on after a throw.");

            secondUid = SEntMan.SpawnEntity("Wrench", SEntMan.GetComponent<TransformComponent>(playerUid).Coordinates);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(playerUid, secondUid));
        });

        // Throwing has a cooldown of its own.
        await Pair.RunTicksSync(40);
        await RequestThrow(target);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedHandsSystem>().IsHolding(playerUid, secondUid), Is.False,
                "Every click in throw mode must throw, not only the first."));

        // The button turns it back off.
        await Client.WaitPost(() =>
            Client.ResolveDependency<IClientEntityManager>().RaisePredictiveEvent(new KsToggleThrowModeEvent()));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() => Assert.That(throwModeSystem.IsEnabled(playerUid), Is.False));
    }

    /// <summary>
    ///     What the client's input system sends for a click in throw mode.
    /// </summary>
    private async Task RequestThrow(EntityCoordinates serverCoordinates)
    {
        var netCoordinates = SEntMan.GetNetCoordinates(serverCoordinates);
        await Client.WaitPost(() =>
            Client.ResolveDependency<IClientEntityManager>().RaisePredictiveEvent(new KsThrowModeThrowEvent(netCoordinates)));
        await Pair.RunTicksSync(5);
    }
}
