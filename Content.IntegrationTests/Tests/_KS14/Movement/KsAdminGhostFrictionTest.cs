#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Systems;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Movement;

/// <summary>
///     Admin ghosts are the one mob that is <c>Kinematic</c> rather than
///         <c>KinematicController</c>, so anything that stops them using mob movement while
///         they still carry velocity drops them into TileFrictionController's
///         non-KinematicController branch, where a DebugAssert used to take the server down.
///         Sitting down at a shuttle console hits it: piloting cancels CanMove.
/// </summary>
public sealed class KsAdminGhostFrictionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task TestPilotingAdminGhostWithVelocityDoesNotAssert()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var physics = entManager.System<SharedPhysicsSystem>();
        var consoles = entManager.System<ShuttleConsoleSystem>();

        var map = await Pair.CreateTestMap();

        var ghost = EntityUid.Invalid;
        var console = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            ghost = entManager.SpawnEntity("AdminObserver", map.GridCoords);
            console = entManager.SpawnEntity("ComputerShuttle", map.GridCoords);

            // Mob movement decays velocity but never to exactly zero, so any ghost that has
            // moved at all is carrying some.
            physics.SetLinearVelocity(ghost, new Vector2(5f, 0f));

            // What ShuttleConsoleSystem.TryPilot does. AddPilot runs UpdateCanMove, and
            // SharedShuttleConsoleSystem cancels it for anyone holding a console, so the
            // mover controller skips the ghost from here on.
            entManager.EnsureComponent<PilotComponent>(ghost);
            consoles.AddPilot(console, ghost, entManager.GetComponent<ShuttleConsoleComponent>(console));
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.TryGetComponent<PilotComponent>(ghost, out var pilot)
                && pilot.Console == console,
                "the admin ghost should still be piloting the console");
        });
    }

    /// <summary>
    ///     The other way in: relaying movement (an FPV drone, a mech, the AI eye) makes
    ///         HandleMobMovement bail before it ever records whether mob movement was used,
    ///         so the ghost falls through to the friction controller the same way.
    /// </summary>
    [Test]
    public async Task TestRelayingAdminGhostWithVelocityDoesNotAssert()
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var physics = entManager.System<SharedPhysicsSystem>();
        var mover = entManager.System<SharedMoverController>();

        var map = await Pair.CreateTestMap();

        var ghost = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            ghost = entManager.SpawnEntity("AdminObserver", map.GridCoords);
            var target = entManager.SpawnEntity("MobObserver", map.GridCoords);

            physics.SetLinearVelocity(ghost, new Vector2(5f, 0f));
            mover.SetRelay(ghost, target);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entManager.HasComponent<RelayInputMoverComponent>(ghost),
                "the admin ghost should still be relaying its movement");
        });
    }
}
