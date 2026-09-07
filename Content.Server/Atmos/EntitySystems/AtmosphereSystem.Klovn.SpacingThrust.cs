// KS14: added in this fork
using System.Numerics;
using System.Runtime.InteropServices;
using Content.Server.Atmos.Components;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.Server.Atmos.EntitySystems;

/// <summary>
///     Reaction thrust from gas crossing a grid's boundary.
/// </summary>
/// <remarks>
///     <para>Atmospherics lets gas flow off the edge of a grid into the map's atmosphere, where it simply stops
///         existing. That gas carries momentum, and dropping it on the floor means a station can vent its entire
///         air supply out of a hull breach without ever noticing. This is a rocket: the momentum the escaping
///         mass carries away has to come out of the grid.</para>
///
///     <para>Every parcel of gas that crosses the boundary is treated as effusing into the neighbouring
///         mixture. Its mean thermal speed is used as the exhaust velocity,
///         v = sqrt(8RT / (pi * M)), so the impulse delivered to the grid is |m| * v, pointed against the flow.</para>
///
///     <para>The sign falls out of the same expression on a map that actually has air
///         (a <see cref="MapAtmosphereComponent"/> whose mixture is not vacuum): gas flowing *in* through a hole
///         pushes the grid towards the hole, exactly as the unbalanced pressure on the opposite hull would, and
///         a grid whose interior is in equilibrium with the map exchanges gas both ways and nets out to nothing.
///         See <see cref="MapAtmosphereComponent.KsGridThrustModifier"/> for the per-map opt-out.</para>
/// </remarks>
public sealed partial class AtmosphereSystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _ksPhysicsQuery = default!;

    /// <summary>
    ///     Whether gas leaving a grid pushes that grid around. See the class remarks.
    /// </summary>
    public bool KsSpacingThrust { get; private set; }

    /// <summary>
    ///     Scales every impulse produced by <see cref="KsSpacingThrust"/>. 1 is the physical value.
    /// </summary>
    public float KsSpacingThrustMultiplier { get; private set; }

    /// <summary>
    ///     Grid-local impulse per grid, accumulated over an atmos tick and flushed in one go by
    ///     <see cref="KsFlushSpacingThrust"/>. Grids are only present here on ticks where they actually leaked.
    /// </summary>
    private readonly Dictionary<EntityUid, KsSpacingThrustAccumulator> _ksSpacingThrustAccumulators = new();

    /// <summary>
    ///     Unit vectors for each <see cref="AtmosDirection"/> index, in grid-local space.
    /// </summary>
    private static readonly Vector2[] KsAtmosDirectionVectors =
    [
        new(0f, 1f),  // North
        new(0f, -1f), // South
        new(1f, 0f),  // East
        new(-1f, 0f), // West
    ];

    /// <summary>
    ///     Impulses below this magnitude, in newton-seconds, are dropped rather than applied.
    ///     Keeps a station from being nudged by rounding error on the thousands of boundary tiles it has.
    /// </summary>
    private const float KsMinimumSpacingImpulse = 0.01f;

    /// <summary>
    ///     Net mass moved by the last <see cref="Share"/> call, in grams.
    ///     Positive means the gas left the receiver towards the sharer.
    /// </summary>
    private float _ksShareMassMoved;

    /// <summary>
    ///     Total mass moved by the last <see cref="Share"/> call in either direction, in grams.
    ///     Divided by <see cref="_ksShareAbsMolesMoved"/> this gives the mean molar mass of the moved gas.
    /// </summary>
    private float _ksShareAbsMassMoved;

    /// <summary>
    ///     Total moles moved by the last <see cref="Share"/> call in either direction.
    /// </summary>
    private float _ksShareAbsMolesMoved;

    private void InitializeKsSpacingThrust()
    {
        Subs.CVar(_cfg, KsCCVars.AtmosSpacingThrust, value => KsSpacingThrust = value, true);
        Subs.CVar(_cfg, KsCCVars.AtmosSpacingThrustMultiplier, value => KsSpacingThrustMultiplier = value, true);
    }

    /// <summary>
    ///     Accumulates the reaction impulse from a LINDA share that crossed the grid boundary.
    ///     Reads the mass bookkeeping <see cref="Share"/> left behind, so it must be called immediately after it.
    /// </summary>
    /// <param name="gridUid">The grid that owns both tiles.</param>
    /// <param name="tile">The tile that was shared from, i.e. the receiver passed to <see cref="Share"/>.</param>
    /// <param name="enemyTile">The tile that was shared with. Exactly one of the two is a map tile.</param>
    /// <param name="direction">The direction pointing from <paramref name="tile"/> to <paramref name="enemyTile"/>.</param>
    private void KsConsiderLindaSpacingThrust(
        EntityUid gridUid,
        TileAtmosphere tile,
        TileAtmosphere enemyTile,
        AtmosDirection direction)
    {
        var massMovedGrams = _ksShareMassMoved;

        if (_ksShareAbsMolesMoved <= 0f)
            return;

        // Which way the gas actually went. `direction` points from the receiver to the sharer.
        var outward = massMovedGrams > 0f;
        var flowDirection = outward ? direction : direction.GetOpposite();

        // The escaping gas leaves at the temperature of whichever mixture it came out of. Archived values are used
        // for the same reason LINDA uses them: they are the state the share was actually solved against.
        var sourceTile = outward ? tile : enemyTile;
        var temperature = sourceTile.AirArchived?.Temperature ?? sourceTile.Air?.Temperature ?? 0f;

        // Torque is taken about the tile that belongs to the grid, not the map tile hanging off its edge.
        var gridTile = tile.MapAtmosphere ? enemyTile : tile;

        KsAccumulateSpacingThrust(gridUid,
            gridTile.GridIndices,
            KsAtmosDirectionVectors[flowDirection.ToIndex()],
            MathF.Abs(massMovedGrams) * 0.001f,
            _ksShareAbsMassMoved / _ksShareAbsMolesMoved * 0.001f,
            temperature);
    }

    /// <summary>
    ///     Accumulates the reaction impulse from gas that an explosive depressurization threw off the edge of the
    ///     grid. Unlike LINDA's steady leak this moves whole tiles at a time, so it is billed per crossing rather
    ///     than lumped at the breach - a room emptying through several holes at once should spin the grid, not
    ///     just shove it.
    /// </summary>
    /// <param name="gridUid">The grid the gas is leaving.</param>
    /// <param name="tile">The last tile on the grid that the gas occupied.</param>
    /// <param name="direction">The direction the gas is travelling in as it leaves.</param>
    /// <param name="moles">How much gas leaves.</param>
    private void KsConsiderMonstermosSpacingThrust(
        EntityUid gridUid,
        TileAtmosphere tile,
        AtmosDirection direction,
        float moles)
    {
        if (!KsSpacingThrust || moles <= 0f || tile.Air is not { } air || direction == AtmosDirection.Invalid)
            return;

        var molarMass = KsGetMeanMolarMass(air);

        KsAccumulateSpacingThrust(gridUid,
            tile.GridIndices,
            KsAtmosDirectionVectors[direction.ToIndex()],
            moles * molarMass,
            molarMass,
            air.Temperature);
    }

    /// <summary>
    ///     Converts a quantity of gas that has left (or entered) the grid into an impulse and stores it against
    ///     the grid until the end of the atmos tick.
    /// </summary>
    /// <param name="gridUid">The grid to push.</param>
    /// <param name="tileIndices">The tile the gas crossed the boundary at. Used for torque.</param>
    /// <param name="flowDirection">Unit vector, in grid-local space, along which the gas travelled.</param>
    /// <param name="massKilograms">The mass of gas that travelled. Must not be negative.</param>
    /// <param name="molarMassKilograms">The mean molar mass of that gas, in kg/mol.</param>
    /// <param name="temperature">The temperature of the mixture the gas came out of, in Kelvin.</param>
    private void KsAccumulateSpacingThrust(
        EntityUid gridUid,
        Vector2i tileIndices,
        Vector2 flowDirection,
        float massKilograms,
        float molarMassKilograms,
        float temperature)
    {
        if (massKilograms <= 0f || molarMassKilograms <= 0f || temperature <= 0f)
            return;

        // Mean speed of a Maxwell-Boltzmann distribution, used as the effusion exhaust velocity.
        var exhaustVelocity = MathF.Sqrt(8f * Atmospherics.R * temperature / (MathF.PI * molarMassKilograms));

        // Newton's third law: the grid gets the momentum the gas didn't.
        var impulse = flowDirection * (-massKilograms * exhaustVelocity * KsSpacingThrustMultiplier);

        ref var accumulator = ref CollectionsMarshal.GetValueRefOrAddDefault(_ksSpacingThrustAccumulators, gridUid, out _);

        accumulator.LinearImpulse += impulse;

        // Taken about the grid's origin here; the shift to the centre of mass happens on flush, where we know it.
        var tileCentre = new Vector2(tileIndices.X + 0.5f, tileIndices.Y + 0.5f);
        accumulator.AngularImpulseAboutOrigin += Vector2Helpers.Cross(tileCentre, impulse);
    }

    /// <summary>
    ///     Applies everything <see cref="KsAccumulateSpacingThrust"/> gathered over this atmos tick to the grid's
    ///     body, as a single impulse. Called once per grid per atmos tick.
    /// </summary>
    /// <param name="ent">The grid whose atmosphere just finished a tick.</param>
    /// <param name="mapAtmosphere">The atmosphere of the map the grid is on, if it has one.</param>
    private void KsFlushSpacingThrust(
        Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent,
        Entity<MapAtmosphereComponent?> mapAtmosphere)
    {
        if (!_ksSpacingThrustAccumulators.Remove(ent.Owner, out var accumulator))
            return;

        var modifier = mapAtmosphere.Comp?.KsGridThrustModifier ?? 1f;
        if (modifier == 0f)
            return;

        // Anchored and static grids are held in place by something other than their own inertia, so venting does
        // nothing to them. Applying an impulse to a non-dynamic body would be silently discarded anyway.
        if (!_ksPhysicsQuery.TryComp(ent.Owner, out var physicsComponent)
            || physicsComponent.BodyType != BodyType.Dynamic)
            return;

        var linearImpulse = accumulator.LinearImpulse * modifier;

        // Shift the torque from the grid's origin to its centre of mass, which is what the body actually spins about.
        // Tile indices are in tiles, so scale them up to metres first.
        var angularImpulse = (accumulator.AngularImpulseAboutOrigin * ent.Comp3.TileSize * modifier)
                             - Vector2Helpers.Cross(physicsComponent.LocalCenter, linearImpulse);

        // A station has thousands of tiles on its boundary, all of them trading rounding error with the map every
        // tick. That should not add up to a shove.
        if (linearImpulse.LengthSquared() < KsMinimumSpacingImpulse * KsMinimumSpacingImpulse
            && MathF.Abs(angularImpulse) < KsMinimumSpacingImpulse)
            return;

        // The accumulator is grid-local, the physics system wants world space.
        var worldRotation = _transformSystem.GetWorldRotation(ent.Comp4);

        _physics.ApplyLinearImpulse(ent.Owner, worldRotation.RotateVec(linearImpulse), body: physicsComponent);
        _physics.ApplyAngularImpulse(ent.Owner, angularImpulse, body: physicsComponent);
    }

    /// <summary>
    ///     The mean molar mass of a mixture, in kg/mol. Zero for an empty mixture.
    /// </summary>
    public float KsGetMeanMolarMass(GasMixture mixture)
    {
        var moles = 0f;
        var massGrams = 0f;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            moles += mixture.Moles[i];
            massGrams += mixture.Moles[i] * GasMolarMasses[i];
        }

        return moles <= 0f ? 0f : massGrams / moles * 0.001f;
    }

    /// <summary>
    ///     Per-grid running total of the impulse owed to a grid for the gas it has lost (or gained) this atmos tick.
    ///     Both members are in grid-local space.
    /// </summary>
    private struct KsSpacingThrustAccumulator
    {
        /// <summary>
        ///     Total impulse, in newton-seconds.
        /// </summary>
        public Vector2 LinearImpulse;

        /// <summary>
        ///     Total angular impulse about the grid's *origin* (not its centre of mass), in newton-second-tiles.
        /// </summary>
        public float AngularImpulseAboutOrigin;
    }
}
