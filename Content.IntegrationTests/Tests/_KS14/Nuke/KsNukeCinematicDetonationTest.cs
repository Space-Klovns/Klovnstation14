using Content.IntegrationTests.Fixtures;
using Content.Server.Nuke;
using Content.Shared.Damage.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Nuke;

/// <summary>
///     Once its cinematic starts, a nuke explodes exactly once, however it leaves the world.
/// </summary>
[TestFixture]
[TestOf(typeof(NukeSystem))]
public sealed class KsNukeCinematicDetonationTest : GameTest
{
    // A real nuke, with a blast that doesn't flatten the test map or end the round.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: NuclearBomb
  id: KsTestNuke
  components:
  - type: Nuke
    totalIntensity: 1
    endRound: false
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _nukePrototype = "KsTestNuke";

    /// <summary>
    ///     Upper bound on the cinematic's lead-up, which is the alarm's remainder or the intro, whichever is longer.
    /// </summary>
    private static readonly TimeSpan MaximumLeadUp = TimeSpan.FromSeconds(60);

    [Test]
    public async Task NukeCannotBeDestroyedDuringItsCinematic()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var nukeUid = server.EntMan.SpawnEntity(_nukePrototype, testMap.GridCoords);
            server.EntMan.System<NukeSystem>().ActivateBomb(nukeUid);

            Assert.That(server.EntMan.HasComponent<GodmodeComponent>(nukeUid),
                "The nuke can be destroyed while everyone watches it blow up.");
        });
    }

    /// <summary>
    ///     Deleting a nuke mid-cinematic still sets it off. The control, a nuke deleted before its cinematic starts,
    ///         shows that deletion alone doesn't.
    /// </summary>
    [Test]
    public async Task NukeDeletedDuringItsCinematicStillExplodes()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var listenerSystem = server.EntMan.System<KsNukeExplodedListenerSystem>();

        await server.WaitAssertion(() =>
        {
            var nukeSystem = server.EntMan.System<NukeSystem>();
            // The count outlives a test on a pair that isn't restarted between them, so only what this test adds counts.
            var countBefore = listenerSystem.ExplodedCount;

            var idleNukeUid = server.EntMan.SpawnEntity(_nukePrototype, testMap.GridCoords);
            server.EntMan.DeleteEntity(idleNukeUid);
            Assert.That(listenerSystem.ExplodedCount - countBefore, Is.Zero, "Deleting a nuke that was never set off exploded it.");

            var nukeUid = server.EntMan.SpawnEntity(_nukePrototype, testMap.GridCoords);
            nukeSystem.ActivateBomb(nukeUid);
            Assert.That(listenerSystem.ExplodedCount - countBefore, Is.Zero, "The nuke exploded before its cinematic reached the explosion.");

            server.EntMan.DeleteEntity(nukeUid);
            Assert.That(listenerSystem.ExplodedCount - countBefore, Is.EqualTo(1), "A nuke deleted during its cinematic never exploded.");
        });
    }

    /// <summary>
    ///     A nuke that goes off normally explodes once: deleting itself afterwards doesn't set off the fallback too.
    /// </summary>
    [Test]
    public async Task NukeExplodesOnceAtTheEndOfItsCinematic()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        var listenerSystem = server.EntMan.System<KsNukeExplodedListenerSystem>();

        // The count outlives a test on a pair that isn't restarted between them, so only what this test adds counts.
        var countBefore = listenerSystem.ExplodedCount;
        var nukeUid = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            nukeUid = server.EntMan.SpawnEntity(_nukePrototype, testMap.GridCoords);
            server.EntMan.System<NukeSystem>().ActivateBomb(nukeUid);
        });

        var tickRate = (int)server.Timing.TickRate;
        for (var i = 0; i < MaximumLeadUp.TotalSeconds && listenerSystem.ExplodedCount == countBefore; i++)
        {
            await server.WaitRunTicks(tickRate);
        }

        await server.WaitRunTicks(tickRate);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(listenerSystem.ExplodedCount - countBefore, Is.EqualTo(1), "The nuke didn't explode exactly once.");
            Assert.That(server.EntMan.Deleted(nukeUid), "The nuke outlived its own explosion.");
        }
    }
}
