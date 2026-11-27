using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.MassDriver.EntitySystems;
using Content.Server.Wires;
using Content.Shared._KS14.MassDriver.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.MassDriver;

public sealed class KsMassDriverStateTest : GameTest
{
    private static readonly EntProtoId Driver = "MassDriver";
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task SecurityWireSynchronizesWhileDriverIsInactive()
    {
        var map = await Pair.CreateTestMap();
        EntityUid uid = default;
        Wire wire = default!;
        MassDriverSecurityWireAction action = default!;
        await Server.WaitPost(() =>
        {
            uid = SEntMan.SpawnEntity(Driver, map.GridCoords);
            SEntMan.RemoveComponent<ActiveMassDriverComponent>(uid);
            wire = SEntMan.GetComponent<WiresComponent>(uid).WiresList.Single(w => w.Action is MassDriverSecurityWireAction);
            action = (MassDriverSecurityWireAction)wire.Action!;
        });

        await RunTicksSync(5);
        var clientUid = ToClientUid(uid);
        await Server.WaitPost(() => action.Cut(uid, wire, SEntMan.GetComponent<MassDriverComponent>(uid)));
        await RunTicksSync(5);
        await Client.WaitAssertion(() => Assert.That(CEntMan.GetComponent<MassDriverComponent>(clientUid).Hacked, Is.True));

        await Server.WaitPost(() => action.Mend(uid, wire, SEntMan.GetComponent<MassDriverComponent>(uid)));
        await RunTicksSync(5);
        await Client.WaitAssertion(() => Assert.That(CEntMan.GetComponent<MassDriverComponent>(clientUid).Hacked, Is.False));
    }
}
