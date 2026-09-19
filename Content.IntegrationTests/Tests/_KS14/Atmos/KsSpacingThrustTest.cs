using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Tests for the recoil a grid gets from gas crossing its boundary.
///     See <c>AtmosphereSystem.Klovn.SpacingThrust.cs</c>.
/// </summary>
[TestFixture]
[TestOf(typeof(AtmosphereSystem))]
public sealed class KsSpacingThrustTest : GameTest
{
    /// <summary>
    ///     Fraction of the impulse the escaping gas carried that a symmetric grid is allowed to end up with.
    /// </summary>
    /// <remarks>
    ///     A square measures 1.1% with monstermos and 1.3% without. That is not billing error and no billing
    ///     scheme removes it: <c>Share</c> works off live moles, so a tile's northward share is drawn from a
    ///     fuller mixture than its westward one, and the grid is genuinely lopsided for as long as it is venting.
    ///     Pricing every boundary off one consistent snapshot of that state was tried and measured 2.0%, worse,
    ///     since it sees the whole accumulated lopsidedness instead of averaging over it.
    ///     Feeding <c>Share</c> the archived moles the way /tg/'s LINDA does takes this to 0.45%, but that is a
    ///     change to the solver every grid in the game runs on, not to this feature.
    /// </remarks>
    private const float SymmetryTolerance = 0.02f;

    private AtmosphereSystem _atmosphereSystem = default!;
    private SharedMapSystem _mapSystem = default!;

    private Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> _processEntity;
    private EntityUid _gridUid;
    private EntityUid _mapUid;

    /// <summary>
    ///     Builds a bare rectangle of plating on an otherwise empty map, gives it an atmosphere and leaves every
    ///     tile of it exposed to space on the outside.
    /// </summary>
    /// <param name="size">Size of the rectangle, in tiles.</param>
    /// <param name="monstermos">Whether to leave monstermos equalization and depressurization enabled.</param>
    private async Task SetupGrid(Vector2i size, bool monstermos)
    {
        _atmosphereSystem = SEntMan.System<AtmosphereSystem>();
        _mapSystem = SEntMan.System<SharedMapSystem>();

        Server.CfgMan.SetCVar(CCVars.MonstermosEqualization, monstermos);
        Server.CfgMan.SetCVar(CCVars.MonstermosDepressurization, monstermos);

        var testMap = await Pair.CreateTestMap();
        _mapUid = testMap.MapUid;
        _gridUid = testMap.Grid.Owner;

        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var plating = new Tile(tileDefinitionManager["Plating"].TileId);

        await Server.WaitPost(() =>
        {
            for (var x = 0; x < size.X; x++)
            {
                for (var y = 0; y < size.Y; y++)
                {
                    _mapSystem.SetTile(_gridUid, testMap.Grid.Comp, new Vector2i(x, y), plating);
                }
            }

            SEntMan.EnsureComponent<GridAtmosphereComponent>(_gridUid);
        });

        // Let the atmosphere revalidate, so every tile has a mixture to fill.
        await Server.WaitRunTicks(5);

        await Server.WaitPost(() =>
        {
            _processEntity = new Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>(
                _gridUid,
                SEntMan.GetComponent<GridAtmosphereComponent>(_gridUid),
                SEntMan.GetComponent<GasTileOverlayComponent>(_gridUid),
                SEntMan.GetComponent<MapGridComponent>(_gridUid),
                SEntMan.GetComponent<TransformComponent>(_gridUid));
        });
    }

    /// <summary>
    ///     Fills one tile with room-temperature air.
    /// </summary>
    private void FillTile(Vector2i indices)
    {
        var mixture = _atmosphereSystem.GetTileMixture(_gridUid, _mapUid, indices, excite: true);
        Assert.That(mixture, Is.Not.Null, $"Tile {indices} has no mixture to fill.");

        mixture!.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        mixture.Temperature = Atmospherics.T20C;
    }

    /// <summary>
    ///     Total moles actually aboard the grid, ignoring the immutable map air hanging off its edges.
    /// </summary>
    private float GetGridMoles()
    {
        var moles = 0f;

        foreach (var tile in _processEntity.Comp1.Tiles.Values)
        {
            if (tile.MapAtmosphere)
                continue;

            moles += tile.Air?.TotalMoles ?? 0f;
        }

        return moles;
    }

    /// <summary>
    ///     The impulse a given amount of vented air would deliver if every last mole of it went the same way.
    ///     Used as the yardstick a symmetric grid's net impulse has to be negligible against.
    /// </summary>
    private float GetGrossImpulse(float moles)
    {
        var reference = new GasMixture();
        reference.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        reference.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

        var molarMass = _atmosphereSystem.KsGetMeanMolarMass(reference);
        var exhaustVelocity = MathF.Sqrt(8f * Atmospherics.R * Atmospherics.T20C / (MathF.PI * molarMass));

        return moles * molarMass * exhaustVelocity * _atmosphereSystem.KsSpacingThrustMultiplier;
    }

    /// <summary>
    ///     Runs atmos cycles until the grid is owed something, so a test does not have to guess how many cycles
    ///     it takes for a tile to start venting.
    /// </summary>
    /// <param name="maxPasses">How many cycles to give up after.</param>
    private Vector2 RunUntilOwed(int maxPasses = 10)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            _atmosphereSystem.RunProcessingFull(_processEntity, (_mapUid, null), _atmosphereSystem.AtmosTickRate);

            if (_atmosphereSystem.TryGetKsSpacingThrust(_gridUid, out var linearImpulse, out _))
                return linearImpulse;
        }

        Assert.Fail($"Venting a tile into space owed the grid nothing after {maxPasses} atmos cycles.");
        return Vector2.Zero;
    }

    /// <summary>
    ///     A square of identical mixtures venting into space is symmetric, so the recoil has to cancel.
    ///     It does not cancel on its own: LINDA shares a tile's neighbours in a fixed north-south-east-west
    ///     order and each share leaves less gas for the next, which is a standing south-west shove if the
    ///     escaping mass is billed as it moves rather than as the cycle's archived state says it should.
    /// </summary>
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public async Task UniformSquareGridGetsNoNetImpulse(bool monstermos)
    {
        await SetupGrid(new Vector2i(5, 5), monstermos);

        await Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    FillTile(new Vector2i(x, y));
                }
            }

            var molesBefore = GetGridMoles();

            for (var i = 0; i < 20; i++)
            {
                _atmosphereSystem.RunProcessingFull(_processEntity, (_mapUid, null), _atmosphereSystem.AtmosTickRate);
            }

            var molesLost = molesBefore - GetGridMoles();
            Assert.That(molesLost, Is.GreaterThan(0f), "The grid never vented, so this test proves nothing.");

            _atmosphereSystem.TryGetKsSpacingThrust(_gridUid, out var linearImpulse, out var angularImpulse);

            var gross = GetGrossImpulse(molesLost);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(linearImpulse.Length(),
                    Is.LessThan(gross * SymmetryTolerance),
                    $"Symmetric grid picked up {linearImpulse} N*s of linear impulse out of {gross} N*s vented.");

                Assert.That(MathF.Abs(angularImpulse),
                    Is.LessThan(gross * SymmetryTolerance),
                    $"Symmetric grid picked up {angularImpulse} N*s*tiles of angular impulse out of {gross} N*s vented.");
            }
        });
    }

    /// <summary>
    ///     The other half of the above: gas leaving through one side has to shove the grid towards the other.
    ///     Air in the west tile of a two-tile grid escapes north, south and west, so only the westward loss is
    ///     left once north and south cancel, and the grid goes east.
    /// </summary>
    [Test]
    public async Task VentingOutOneSidePushesTheOtherWay()
    {
        await SetupGrid(new Vector2i(2, 1), monstermos: false);

        await Server.WaitAssertion(() =>
        {
            FillTile(new Vector2i(0, 0));

            var linearImpulse = RunUntilOwed();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(linearImpulse.X, Is.GreaterThan(0f), $"Grid was pushed the wrong way: {linearImpulse}.");

                // Within about fifteen degrees of due east, i.e. north and south did cancel and what is left is
                // the westward vent. Measures better than 12:1.
                Assert.That(linearImpulse.X,
                    Is.GreaterThan(MathF.Abs(linearImpulse.Y) * 4f),
                    $"North and south venting failed to cancel: {linearImpulse}.");
            }
        });
    }

    /// <summary>
    ///     Reservoirs are keyed by uid rather than held on a component, and uids come back around between rounds,
    ///     so anything still owed at the end of one has to be dropped rather than paid to whatever inherits the uid.
    /// </summary>
    [Test]
    public async Task RoundRestartClearsReservoirs()
    {
        await SetupGrid(new Vector2i(2, 1), monstermos: false);

        await Server.WaitAssertion(() =>
        {
            FillTile(new Vector2i(0, 0));

            RunUntilOwed();
            Assert.That(_atmosphereSystem.KsSpacingThrustGridCount, Is.GreaterThan(0));

            SEntMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());

            Assert.That(_atmosphereSystem.KsSpacingThrustGridCount, Is.Zero);
        });
    }
}
