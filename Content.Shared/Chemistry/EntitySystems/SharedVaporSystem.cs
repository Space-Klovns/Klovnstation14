using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Chemistry.PredictedTileReactions;
using Content.Shared._KS14.PredictedSpawning;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Content.Shared.Vapor;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Shared.Chemistry.EntitySystems;

/// <summary>
///     Moves a vapor and reacts its reagents with each tile it crosses. Shared, so that a player's client predicts its
///         own vapors: the server simulates every vapor, and a client only its own kept copies of the ones it sprayed or
///         shot (see <see cref="KsSharedPredictedSpawnSystem"/>), with the tile reactions it has
///         (see <see cref="KsPredictedTileReactionSystem"/>).
/// </summary>
/// <remarks>
///     Moved here from the server's <c>VaporSystem</c> (KS14), which keeps what is not predicted: what a vapor does to
///         the entities it touches, and its heat exchange with puddles.
/// </remarks>
public abstract partial class SharedVaporSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] protected SharedSolutionContainerSystem SolutionContainerSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] protected SharedPuddleSystem PuddleSystem = default!;
    [Dependency] private KsSharedPredictedSpawnSystem _predictedSpawnSystem = default!;
    [Dependency] private KsPredictedTileReactionSystem _predictedTileReactionSystem = default!;

    [Dependency] private EntityQuery<SolutionComponent> _solutionQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Before the step: a vapor reacts with the tile the last tick left it on, so a tile it enters on the tick it
        //     despawns is never reacted with. A client's kept copy has to do the same, or it reacts with a tile the
        //     server's vapor never does.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    /// <summary>
    ///     Whether this side simulates <paramref name="uid"/>: every vapor on the server, and on a client only its own
    ///         kept copies. Anyone else's vapor is the server's, drawn as it is sent.
    /// </summary>
    protected virtual bool IsSimulatedHere(EntityUid uid) => true;

    /// <summary>
    ///     Whether a vapor may react with a tile and be deleted now, rather than at its next update. Not while a client
    ///         applies a state: reacting can spawn a puddle, and a kept copy's collisions are raised there too.
    /// </summary>
    protected virtual bool CanStopNow => true;

    /// <summary>
    ///     What a vapor does to an entity it touches. Not predicted: the server's alone.
    /// </summary>
    protected virtual void OnTouch(Entity<VaporComponent> vapor, EntityUid touchedUid, Solution solution)
    {
    }

    /// <summary>
    ///     Runs before a vapor's reagents react with a tile it has reached.
    /// </summary>
    protected virtual void BeforeTileReactions(Entity<SolutionComponent> vapor, TileRef tile)
    {
    }

    [SubscribeLocalEvent]
    private void OnStartCollide(Entity<VaporComponent> entity, ref StartCollideEvent args)
    {
        if (!IsSimulatedHere(entity) || !_solutionQuery.TryComp(entity, out var solutionComponent))
            return;

        OnTouch(entity, args.OtherEntity, solutionComponent.Solution);

        // Check for collision with a impassable object (e.g. wall) and stop
        if ((args.OtherFixture.CollisionLayer & (int)CollisionGroup.Impassable) == 0 || !args.OtherFixture.Hard)
            return;

        if (!CanStopNow)
        {
            entity.Comp.KsStopPending = true;
            return;
        }

        Stop(entity);
    }

    /// <summary>
    ///     A vapor stopped against a wall: what it still carries lands where it stops, rather than vanishing.
    /// </summary>
    private void Stop(Entity<VaporComponent> entity)
    {
        KsReactRemainder(entity);
        _predictedSpawnSystem.PredictedQueueDelDeferrable(entity);
    }

    public void Start(Entity<VaporComponent?> vapor,
        TransformComponent vaporXform,
        Vector2 dir,
        float speed,
        MapCoordinates target,
        float aliveTime,
        EntityUid? user = null,
        bool predicted = false)
    {
        if (!Resolve(vapor, ref vapor.Comp))
            return;

        vapor.Comp.Active = true;
        var despawn = EnsureComp<TimedDespawnComponent>(vapor);
        despawn.Lifetime = aliveTime;

        // Set Move
        if (_physicsQuery.TryComp(vapor, out var physics))
        {
            _physicsSystem.SetLinearDamping(vapor, physics, 0f);
            _physicsSystem.SetAngularDamping(vapor, physics, 0f);

            // Predicted when the sprayer's client sprayed it too, so its recoil is the client's and not sent twice.
            _throwingSystem.TryThrow(vapor, dir, speed, user: user, predicted: predicted);

            var distance = (target.Position - _transformSystem.GetWorldPosition(vaporXform)).Length();
            var time = (distance / physics.LinearVelocity.Length());
            despawn.Lifetime = MathF.Min(aliveTime, time);
        }
    }

    /// <summary>
    ///     Moves <paramref name="split"/> of <paramref name="solution"/> into the vapor, and colours the vapor to match.
    /// </summary>
    public bool TryAddSolution(Entity<SolutionComponent?> vapor, Entity<SolutionComponent> solution, FixedPoint2 split)
    {
        if (solution.Comp.Solution.Volume <= 0 || split <= 0 || !_solutionQuery.Resolve(vapor.Owner, ref vapor.Comp))
            return false;

        var newSolution = SolutionContainerSystem.SplitSolution(solution, split);

        if (TryComp<AppearanceComponent>(vapor, out var appearance))
        {
            _appearanceSystem.SetData(vapor, VaporVisuals.Color, newSolution.GetColor(ProtoMan).WithAlpha(1f), appearance);
            _appearanceSystem.SetData(vapor, VaporVisuals.State, true, appearance);
        }

        return SolutionContainerSystem.TryAddSolution((vapor.Owner, vapor.Comp), newSolution);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A client's kept copy moves only on these, so this reacts once per tile there too. Always true on the server.
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        // TODO: Vapor should just use SolutionComponent and not be capable of having multiple solutions.
        var query = EntityQueryEnumerator<VaporComponent, SolutionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vaporComp, out var solution, out var xform))
        {
            // Return early if we're not active
            if (!vaporComp.Active || !IsSimulatedHere(uid))
                continue;

            if (vaporComp.KsStopPending)
            {
                vaporComp.KsStopPending = false;
                Stop((uid, vaporComp));
                continue;
            }

            // Get the current location of the vapor entity first
            if (xform.GridUid is not { } gridUid || !_mapGridQuery.TryComp(gridUid, out var gridComp))
                continue;

            var tile = _mapSystem.GetTileRef(gridUid, gridComp, xform.Coordinates);

            // Check if the tile is a tile we've reacted with previously. If so, skip it.
            // If we have no previous tile reference, we don't return so we can save one.
            if (vaporComp.PreviousTileRef != null && tile == vaporComp.PreviousTileRef)
                continue;

            BeforeTileReactions((uid, solution), tile);

            ReactWithTile((uid, solution), tile, vaporComp.TransferAmountPercentage, vaporComp.MinimumTransferAmount);

            // Delete the vapor entity if it has no contents
            if (solution.Solution.Volume == 0)
                _predictedSpawnSystem.PredictedQueueDelDeferrable(uid);

            // Set the previous tile reference to the current tile
            vaporComp.PreviousTileRef = tile;
        }
    }

    /// <summary>
    ///     Reacts each of the vapor's reagents with <paramref name="tile"/>, offering it <paramref name="transferFraction"/>
    ///         of what the vapor carries but at least <paramref name="minimumTransfer"/>, and removes what the reactions
    ///         used. Each reagent may have its own tile reactions; this is not doing chemical reactions.
    /// </summary>
    private void ReactWithTile(Entity<SolutionComponent> vapor, TileRef tile, float transferFraction, float minimumTransfer)
    {
        var contents = vapor.Comp.Solution;

        // Tile reactions that spill read the vapor's temperature; on a client, only a kept vapor's reactions act.
        using var temperatureScope = PuddleSystem.KsBeginTileReactionTemperature(contents.Temperature);
        using var predictedScope = _predictedTileReactionSystem.BeginScope();

        foreach (var reagentQuantity in contents.Contents.ToArray())
        {
            // Check if the reagent is empty
            if (reagentQuantity.Quantity == FixedPoint2.Zero)
                continue;

            var reagent = ProtoMan.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);

            // Limit the reaction amount to a minimum value to ensure no floating point funnies.
            // Ex: A solution with a low percentage transfer amount will slowly approach 0.01... and never get deleted
            var clampedAmount = Math.Max((float)reagentQuantity.Quantity * transferFraction, minimumTransfer);

            // Preform the reagent's TileReaction
            var reaction = reagent.ReactionTile(tile, clampedAmount, EntityManager, reagentQuantity.Reagent.Data);

            if (reaction > reagentQuantity.Quantity)
                reaction = reagentQuantity.Quantity;

            SolutionContainerSystem.RemoveReagent(vapor, reagentQuantity.Reagent, reaction);
        }
    }
}
