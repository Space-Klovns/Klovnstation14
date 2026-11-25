using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.MassDriver.EntitySystems;
using Content.Server._Starlight.Plumbing.EntitySystems;
using Content.Server.Doors.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Performance;

[TestFixture]
public sealed class KsIdleUpdateTest : GameTest
{
    [Test]
    public async Task IdleMachineUpdatesAllocateNothing()
    {
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Measure(SEntMan.System<DoorSystem>());
                Measure(SEntMan.System<MassDriverSystem>());
                Measure(SEntMan.System<PlumbingDeviceSystem>());
            });
        });
    }

    private static void Measure(EntitySystem system)
    {
        const float dt = 1f / 60f;
        system.Update(dt);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; i++)
            system.Update(dt);

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"{system.GetType().Name}: {bytes} bytes in 1024 idle ticks");
        Assert.That(bytes, Is.Zero, system.GetType().Name);
    }
}
