using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Lock;

namespace Content.IntegrationTests.Tests._KS14.Construction;

/// <summary>
///     An ID-locked shuttle console can only be taken apart while it is unlocked, otherwise anyone with a
///         crowbar could pry the board out of a console that is locked to its owners.
/// </summary>
public sealed class KsLockedShuttleConsoleDeconstructionTest : InteractionTest
{
    private const string IdLockedConsole = "KsComputerShuttleIdLocked";
    private const string ComputerFrame = "ComputerFrame";

    [Test]
    public async Task LockedConsoleCannotBePried()
    {
        await SpawnTarget(IdLockedConsole);
        await SetLocked(true);

        await InteractUsing(Pry);

        AssertPrototype(IdLockedConsole);
    }

    [Test]
    public async Task UnlockedConsoleCanBePried()
    {
        await SpawnTarget(IdLockedConsole);
        await SetLocked(false);

        await InteractUsing(Pry);

        AssertPrototype(ComputerFrame);
    }

    [Test]
    public async Task LockedConsoleCannotBeUnanchored()
    {
        await SpawnTarget(IdLockedConsole);
        await SetLocked(true);

        await InteractUsing(Wrench);

        Assert.That(Xform(Target!.Value).Anchored, Is.True, "a locked console must stay anchored");
    }

    private async Task SetLocked(bool locked)
    {
        await Server.WaitPost(() =>
        {
            var lockSystem = SEntMan.System<LockSystem>();
            var consoleUid = ToServer(Target!.Value);

            if (locked)
                lockSystem.Lock(consoleUid, user: null);
            else
                lockSystem.Unlock(consoleUid, user: null);
        });
    }
}
