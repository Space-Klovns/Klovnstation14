#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

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
    ///     Stops the pool failing the test over server error logs, for tests that deliberately provoke a
    ///         rejection. Dispose the result to stop tolerating them again.
    /// </summary>
    /// <remarks>
    ///     The handler's finer-grained JudgeLog hook would be preferable, but its signature is in terms of a
    ///         Serilog type this project does not reference, so this raises the bar to Fatal for the scope
    ///         instead. Keep these scopes tight.
    /// </remarks>
    protected IDisposable ExpectServerErrors()
    {
        var handler = Pair.ServerLogHandler;
        var previousFailureLevel = handler.FailureLevel;
        handler.FailureLevel = LogLevel.Fatal;

        return new ExpectedErrorScope(() => handler.FailureLevel = previousFailureLevel);
    }

    private sealed class ExpectedErrorScope(Action onDispose) : IDisposable
    {
        public void Dispose()
        {
            onDispose();
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

        Assert.That(stack, Is.Not.Null, "failed to build the test z-level stack");
        return stack!;
    }
}
