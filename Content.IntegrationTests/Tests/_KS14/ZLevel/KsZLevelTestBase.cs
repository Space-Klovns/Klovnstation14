#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;
using Serilog.Events;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     Builds the two-z-level stack the transit tests fall through, and pins every tuning cvar so that a
///         config change elsewhere cannot quietly rewrite what these tests assert.
/// </summary>
public abstract class KsZLevelTestBase : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    /// <summary>
    ///     Round numbers, chosen so the kinematics work out exactly: a fall of one z-level from rest takes
    ///         sqrt(2 * 1 / 8) = 0.5 seconds.
    /// </summary>
    protected const float TestGravity = 8f;
    protected const float TestTerminalVelocity = 100f;
    protected const float TestImpactVelocity = 3f;

    /// <summary>
    ///     The upper z-level has floor only on tile (0, 0); the lower one also has floor under the hole, at
    ///         tile (2, 0). So an entity walked off (0, 0) towards (2, 0) has nothing under it on the level it
    ///         is on, and something to land on one level down.
    /// </summary>
    protected static readonly Vector2i FloorTile = new(0, 0);
    protected static readonly Vector2i HoleTile = new(2, 0);

    protected sealed record TestStack(
        EntityUid UpperMapUid,
        MapId UpperMapId,
        Entity<MapGridComponent> UpperGrid,
        EntityUid LowerMapUid,
        MapId LowerMapId,
        Entity<MapGridComponent> LowerGrid)
    {
        /// <summary>Standing on solid floor, on the upper z-level.</summary>
        public EntityCoordinates SupportedCoords => new(UpperGrid.Owner, 0.5f, 0.5f);

        /// <summary>Over the hole in the upper z-level, with floor waiting one level down.</summary>
        public EntityCoordinates HoleCoords => new(UpperGrid.Owner, 2.5f, 0.5f);

        /// <summary>Directly under the upper z-level's floor, on the lower one.</summary>
        public EntityCoordinates UnderFloorCoords => new(LowerGrid.Owner, 0.5f, 0.5f);

        /// <summary>Directly under the hole, on the lower z-level.</summary>
        public EntityCoordinates UnderHoleCoords => new(LowerGrid.Owner, 2.5f, 0.5f);
    }

    /// <summary>
    ///     Tolerates one specific server error log, for a test that deliberately provokes a rejection, and
    ///         counts how many times it was seen so the test can assert the rejection really was loud.
    /// </summary>
    /// <remarks>
    ///     Matched on both sawmill and message text on purpose: every other error log still fails the test, the
    ///         way it should. Dispose the scope to stop tolerating it.
    /// </remarks>
    protected ExpectedErrorScope ExpectServerError(string sawmillName, string messageFragment)
    {
        return new ExpectedErrorScope(Pair.ServerLogHandler, sawmillName, messageFragment);
    }

    protected sealed class ExpectedErrorScope : IDisposable
    {
        private readonly PoolTestLogHandler _handler;
        private readonly Func<string, LogEvent, bool> _judge;

        /// <summary>
        ///     How many error logs this scope has recognised and let through.
        /// </summary>
        public int Matches { get; private set; }

        public ExpectedErrorScope(PoolTestLogHandler handler, string sawmillName, string messageFragment)
        {
            _handler = handler;
            _judge = (name, message) =>
            {
                if (name != sawmillName || !message.RenderMessage().Contains(messageFragment, StringComparison.Ordinal))
                    return false;

                Matches++;
                return true;
            };

            _handler.JudgeLog += _judge;
        }

        public void Dispose()
        {
            _handler.JudgeLog -= _judge;
        }
    }

    /// <summary>
    ///     Pins the transit cvars to <see cref="TestGravity"/> and friends.
    /// </summary>
    protected async Task OverrideTransitCVars(float gravity = TestGravity)
    {
        await OverrideCVar(Side.Server, KsCCVars.ZLevelTransitGravity, gravity);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelTransitTerminalVelocity, TestTerminalVelocity);
        await OverrideCVar(Side.Server, KsCCVars.ZLevelTransitImpactVelocity, TestImpactVelocity);
    }

    /// <summary>
    ///     Creates two linked z-levels, the second directly under the first.
    /// </summary>
    /// <param name="gravity">Whether either map has gravity at all. False leaves entities coasting.</param>
    protected async Task<TestStack> CreateStack(bool gravity = true)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var zLevelSystem = entManager.System<KsZLevelSystem>();

        var tile = new Tile(tileDefinitionManager["Plating"].TileId);
        TestStack? stack = null;

        await server.WaitPost(() =>
        {
            var upperMapUid = mapSystem.CreateMap(out var upperMapId);
            var upperGrid = mapSystem.CreateGridEntity(upperMapId);
            mapSystem.SetTile(upperGrid.Owner, upperGrid.Comp, FloorTile, tile);

            var lowerMapUid = mapSystem.CreateMap(out var lowerMapId);
            var lowerGrid = mapSystem.CreateGridEntity(lowerMapId);
            mapSystem.SetTile(lowerGrid.Owner, lowerGrid.Comp, FloorTile, tile);
            mapSystem.SetTile(lowerGrid.Owner, lowerGrid.Comp, HoleTile, tile);

            if (gravity)
            {
                foreach (var mapUid in new[] { upperMapUid, lowerMapUid })
                {
                    var gravityComponent = entManager.EnsureComponent<GravityComponent>(mapUid);
                    gravityComponent.Enabled = true;

                    // Otherwise GravitySystem would switch it back off for want of a generator.
                    gravityComponent.Inherent = true;
                }
            }

            entManager.EnsureComponent<KsZLevelComponent>(upperMapUid);
            entManager.EnsureComponent<KsZLevelComponent>(lowerMapUid);
            zLevelSystem.AddZLevelDirectlyUnder(upperMapUid, lowerMapUid);

            stack = new TestStack(upperMapUid, upperMapId, upperGrid, lowerMapUid, lowerMapId, lowerGrid);
        });

        Assert.That(stack, Is.Not.Null,
            "the two-z-level test stack was never built, so nothing below depends on it can be trusted");
        return stack!;
    }
}
