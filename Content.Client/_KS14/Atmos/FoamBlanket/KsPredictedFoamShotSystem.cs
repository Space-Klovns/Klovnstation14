using Content.Client._KS14.PredictedSpawning;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Atmos.FoamBlanket;

/// <summary>
///     Lays foam blankets along the path of the shooter's own predicted extinguisher shots, as the server's vapor will
///         a moment later. See <see cref="KsPredictedFoamShotComponent"/>.
/// </summary>
/// <remarks>
///     Only the shooter's kept copy of the shot lays anything (<see cref="KsKeptPredictedSpawnComponent"/>). The
///         re-predicted throwaways of the same shot are deleted within their tick, and the server's copy is hidden
///         here and lays its own blankets on the server.
///     A kept copy is never re-predicted, so neither is anything it does: it moves once per real tick, on the
///         first-time-predicted ones, and the blankets it lays would be lost at the next state reset if they were
///         engine predicted spawns, since nothing would spawn them again. So it checks its tile once per real tick
///         and lays placeholders the client keeps until the server's blankets arrive
///         (<see cref="KsFoamBlanketPlaceholderComponent"/>).
///     It checks before physics steps, as the server's vapor does: a tile entered on a step reacts on the next tick,
///         so a shot that despawns on the tick it enters a tile never lays anything there on the server, and must not
///         here either, or that tile's placeholder waits for a blanket that never comes.
/// </remarks>
public sealed partial class KsPredictedFoamShotSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedKsFoamBlanketSystem _foamBlanketSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<SolutionAmmoProviderComponent> _solutionAmmoProviderQuery = default!;

    /// <summary>
    ///     Units of foam a vapor offers the tile it has just reached: the fraction of what it carries, but not less
    ///         than the minimum, and never more than it carries. The server's <c>VaporSystem</c> offers the first two
    ///         and only ever removes what a reaction used, which is how the third comes about.
    /// </summary>
    public static float GetTileOffer(float foam, float transferFraction, float minimumTransfer)
        => MathF.Min(MathF.Max(foam * transferFraction, minimumTransfer), foam);

    public override void Initialize()
    {
        base.Initialize();

        // Before the step, as the server's vapor reacts: the tile checked is where the last tick left the shot. After
        //      the kept copy's simulated position is restored, so that is not the rendered, interpolated one.
        UpdatesAfter.Add(typeof(KsPredictedSpawnSystem));
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A kept copy only steps on these, so checking on any other would check the same place again.
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        var enumerator = EntityQueryEnumerator<KsPredictedFoamShotComponent, KsKeptPredictedSpawnComponent, ProjectileComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var shotComponent, out _, out var projectileComponent, out var transformComponent))
        {
            if (shotComponent.State == KsPredictedFoamShotState.Done)
                continue;

            var shot = new Entity<KsPredictedFoamShotComponent>(uid, shotComponent);

            if (shotComponent.State == KsPredictedFoamShotState.Waiting && !TryTakeFoam(shot, projectileComponent))
                continue;

            if (transformComponent.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
                continue;

            var tileRef = _mapSystem.GetTileRef(gridUid, mapGridComponent, transformComponent.Coordinates);
            if (shotComponent.LastGridUid == gridUid && shotComponent.LastTile == tileRef.GridIndices)
                continue;

            shotComponent.LastGridUid = gridUid;
            shotComponent.LastTile = tileRef.GridIndices;

            if (tileRef.Tile.IsEmpty)
                continue;

            var offer = GetTileOffer(shotComponent.Foam, shotComponent.TransferFraction, shotComponent.MinimumTransfer);
            var used = _foamBlanketSystem.Deploy(shotComponent.BlanketPrototype, tileRef, offer, playSound: false, placeholder: true);

            shotComponent.Foam -= used;
            if (shotComponent.Foam <= 0f)
                shotComponent.State = KsPredictedFoamShotState.Done;
        }
    }

    /// <summary>
    ///     Works out how much foam the shot carries from the gun that fired it: the gun's fire cost, times the share
    ///         of its solution that is foam. Stays waiting until the gun is known, and gives up if it has no foam.
    /// </summary>
    /// <returns>Whether the shot now carries foam.</returns>
    private bool TryTakeFoam(Entity<KsPredictedFoamShotComponent> shot, ProjectileComponent projectileComponent)
    {
        // The gun is recorded just after the shot spawns, not at its startup.
        if (projectileComponent.Weapon is not { } gunUid)
            return false;

        if (!_solutionAmmoProviderQuery.TryComp(gunUid, out var ammoProviderComponent) ||
            !_solutionContainerSystem.TryGetSolution(gunUid, ammoProviderComponent.SolutionId, out _, out var solution) ||
            solution.Volume <= 0)
        {
            shot.Comp.State = KsPredictedFoamShotState.Done;
            return false;
        }

        // The share rather than the quantity: the solution may or may not have had this shot taken from it yet.
        var share = (float)(solution.GetTotalPrototypeQuantity(shot.Comp.FoamReagent) / solution.Volume);
        shot.Comp.Foam = ammoProviderComponent.FireCost * share;
        shot.Comp.State = shot.Comp.Foam > 0f ? KsPredictedFoamShotState.Laying : KsPredictedFoamShotState.Done;

        return shot.Comp.State == KsPredictedFoamShotState.Laying;
    }
}
