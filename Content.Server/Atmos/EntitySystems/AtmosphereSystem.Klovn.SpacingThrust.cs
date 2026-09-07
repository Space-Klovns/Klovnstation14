// KS14: added in this fork
using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

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
///
///     <para>Impulses are not handed to the body as they are worked out. Atmospherics is happy to empty a whole
///         room in a single monstermos pass, which would be a jolt rather than a shove, so each grid gets a
///         reservoir of owed momentum that drains at a fixed rate. Nothing is ever capped or thrown away on the
///         way through - see <see cref="UpdateKsSpacingThrust"/> - so the total impulse the grid receives is the
///         total the escaping gas took with it, just spread over a second or so instead of a single tick.</para>
/// </remarks>
public sealed partial class AtmosphereSystem
{
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _ksPhysicsQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _ksGridQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _ksTransformQuery = default!;

    /// <summary>
    ///     Whether gas leaving a grid pushes that grid around. See the class remarks.
    /// </summary>
    public bool KsSpacingThrust { get; private set; }

    /// <summary>
    ///     Scales every impulse produced by <see cref="KsSpacingThrust"/>. 1 is the impulse the gas really carries,
    ///     which is far too much against a grid's physics mass - see <see cref="KsCCVars.AtmosSpacingThrustMultiplier"/>.
    /// </summary>
    public float KsSpacingThrustMultiplier { get; private set; }

    /// <summary>
    ///     Time constant of the reservoir drain, in seconds. See <see cref="UpdateKsSpacingThrust"/>.
    /// </summary>
    public float KsSpacingThrustSmoothing { get; private set; }

    /// <summary>
    ///     Momentum owed to each grid for the gas it has lost, in grid-local space. Grids appear here when they
    ///     start leaking and stay until <see cref="UpdateKsSpacingThrust"/> has paid the whole reservoir out.
    /// </summary>
    private readonly Dictionary<EntityUid, KsSpacingThrustAccumulator> _ksSpacingThrustAccumulators = new();

    /// <summary>
    ///     Scratch list of the grids in <see cref="_ksSpacingThrustAccumulators"/>, so the reservoir can be drained
    ///     without enumerating the dictionary we are removing from.
    /// </summary>
    private readonly List<EntityUid> _ksSpacingThrustGrids = new();

    /// <summary>
    ///     When a reservoir has less than this much left in it, in newton-seconds, it is paid out in full and
    ///     closed rather than chased down an asymptote forever. Also the smallest slice worth waking a body for:
    ///     anything under it is held back for the next frame instead, never discarded.
    /// </summary>
    private const float KsMinimumSpacingImpulse = 0.01f;

    private void InitializeKsSpacingThrust()
    {
        Subs.CVar(_cfg, KsCCVars.AtmosSpacingThrust, value => KsSpacingThrust = value, true);
        Subs.CVar(_cfg, KsCCVars.AtmosSpacingThrustMultiplier, value => KsSpacingThrustMultiplier = value, true);
        Subs.CVar(_cfg, KsCCVars.AtmosSpacingThrustSmoothing, value => KsSpacingThrustSmoothing = value, true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnKsRoundRestartCleanup);
    }

    /// <summary>
    ///     Drops every outstanding reservoir when the round ends.
    /// </summary>
    /// <remarks>
    ///     Reservoirs are keyed by <see cref="EntityUid"/> rather than held on a component, and uids are recycled
    ///     between rounds, so anything left here would be paid out to whichever entity inherits the uid next round.
    /// </remarks>
    private void OnKsRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ksSpacingThrustAccumulators.Clear();
        _ksSpacingThrustAccumulators.TrimExcess();

        _ksSpacingThrustGrids.Clear();
        _ksSpacingThrustGrids.TrimExcess();
    }

    /// <summary>
    ///     How many grids are currently owed an impulse. For tests.
    /// </summary>
    public int KsSpacingThrustGridCount => _ksSpacingThrustAccumulators.Count;

    /// <summary>
    ///     The impulse a grid has not been handed yet, in grid-local space. For tests.
    /// </summary>
    /// <param name="gridUid">The grid to look up.</param>
    /// <param name="linearImpulse">Outstanding linear impulse, in newton-seconds.</param>
    /// <param name="angularImpulseAboutOrigin">
    ///     Outstanding angular impulse about the grid's origin, in newton-second-tiles.
    /// </param>
    /// <returns>False if the grid is owed nothing.</returns>
    public bool TryGetKsSpacingThrust(EntityUid gridUid, out Vector2 linearImpulse, out float angularImpulseAboutOrigin)
    {
        if (!_ksSpacingThrustAccumulators.TryGetValue(gridUid, out var accumulator))
        {
            linearImpulse = Vector2.Zero;
            angularImpulseAboutOrigin = 0f;
            return false;
        }

        linearImpulse = accumulator.LinearImpulse;
        angularImpulseAboutOrigin = accumulator.AngularImpulseAboutOrigin;
        return true;
    }

    /// <summary>
    ///     Accumulates the reaction impulse from a LINDA share that crossed the grid boundary.
    ///     Must be called immediately after the <see cref="Share"/> that crossed it.
    /// </summary>
    /// <remarks>
    ///     <para>The flux billed here is worked out from the two tiles' <em>archived</em> mixtures rather than from
    ///         what <see cref="Share"/> actually moved, which matters more than it sounds.</para>
    ///
    ///     <para><see cref="ProcessCell"/> walks a tile's neighbours in a fixed order - north, south, east, west -
    ///         and each share it makes changes the mixture the next one is solved against. The gas that leaves
    ///         northward is therefore always drawn from a fuller tile than the gas that leaves westward, by as much
    ///         as 2:1. Billing the moved mass directly turns that solver artifact into a standing force: a perfectly
    ///         square grid with the same mixture on every tile drifts south-west forever.</para>
    ///
    ///     <para>The archived mixtures are the state the whole cycle is solved against, and for the tile being
    ///         processed they are snapshotted before any of its four shares, so the flux they imply is the same in
    ///         every direction and symmetric geometry cancels. What is left over is the order the tiles themselves
    ///         are archived in, which is worth about 1% of the vented impulse and points somewhere different every
    ///         run rather than accumulating - see <c>KsSpacingThrustTest</c>.</para>
    ///
    ///     <para>The cost of all this is that the figure billed is the flux LINDA intends over the cycle rather
    ///         than the mass it ends up moving, which runs a little high. The two differ only by the solver's own
    ///         within-cycle ordering, and <see cref="KsSpacingThrustMultiplier"/> is the knob for magnitude.</para>
    /// </remarks>
    /// <param name="gridUid">The grid that owns both tiles.</param>
    /// <param name="tile">The tile that was shared from, i.e. the receiver passed to <see cref="Share"/>.</param>
    /// <param name="enemyTile">The tile that was shared with. Exactly one of the two is a map tile.</param>
    /// <param name="direction">The direction pointing from <paramref name="tile"/> to <paramref name="enemyTile"/>.</param>
    /// <param name="atmosAdjacentTurfs">
    ///     The neighbour count <see cref="Share"/> divided by, i.e. the same value it was passed.
    /// </param>
    private void KsConsiderLindaSpacingThrust(
        EntityUid gridUid,
        TileAtmosphere tile,
        TileAtmosphere enemyTile,
        AtmosDirection direction,
        int atmosAdjacentTurfs)
    {
        if (tile.AirArchived is not { } receiverArchived || enemyTile.AirArchived is not { } sharerArchived)
            return;

        var molesMoved = 0f;
        var massMovedGrams = 0f;
        var absMassMovedGrams = 0f;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            // Share's own expression, against the archived mixtures. Positive means the gas goes receiver -> sharer.
            var delta = (receiverArchived.Moles[i] - sharerArchived.Moles[i]) / (atmosAdjacentTurfs + 1);
            if (MathF.Abs(delta) < Atmospherics.GasMinMoles)
                continue;

            var deltaGrams = delta * GasMolarMasses[i];

            molesMoved += MathF.Abs(delta);
            massMovedGrams += deltaGrams;
            absMassMovedGrams += MathF.Abs(deltaGrams);
        }

        if (molesMoved <= 0f)
            return;

        // Which way the gas went on net. `direction` points from the receiver to the sharer.
        var outward = massMovedGrams > 0f;
        var flowDirection = outward ? direction : direction.GetOpposite();

        // The escaping gas leaves at the temperature of whichever mixture it came out of.
        var sourceArchived = outward ? receiverArchived : sharerArchived;

        // Torque is taken about the tile that belongs to the grid, not the map tile hanging off its edge.
        var gridTile = tile.MapAtmosphere ? enemyTile : tile;

        KsAccumulateSpacingThrust(gridUid,
            gridTile.GridIndices,
            flowDirection.CardinalToVec(),
            MathF.Abs(massMovedGrams) * 0.001f,
            absMassMovedGrams / molesMoved * 0.001f,
            sourceArchived.Temperature);
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
            direction.CardinalToVec(),
            moles * molarMass,
            molarMass,
            air.Temperature);
    }

    /// <summary>
    ///     Converts a quantity of gas that has left (or entered) the grid into an impulse and adds it to what
    ///     the grid is owed. <see cref="UpdateKsSpacingThrust"/> pays that out over the following frames.
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

        // Taken about the grid's origin here; the shift to the centre of mass happens on payout, where we know it.
        var tileCentre = new Vector2(tileIndices.X + 0.5f, tileIndices.Y + 0.5f);
        accumulator.AngularImpulseAboutOrigin += Vector2Helpers.Cross(tileCentre, impulse);
    }

    /// <summary>
    ///     Pays out the momentum owed to every leaking grid, a slice at a time.
    /// </summary>
    /// <remarks>
    ///     <para>The reservoir drains exponentially: each frame hands over the fraction
    ///         <c>1 - e^(-dt / tau)</c> of what is left, so a breach arrives as a shove that builds and fades
    ///         rather than as a single-frame jolt. tau is <see cref="KsSpacingThrustSmoothing"/>.</para>
    ///
    ///     <para>Every path through here either applies a slice or leaves it in the reservoir for the next frame -
    ///         nothing is capped, clamped or rounded away - so a grid ends up with exactly the impulse its escaping
    ///         gas carried off, no matter how the frames fall. The one exception is a grid that cannot be pushed
    ///         at all (see below), whose reservoir is dropped rather than paid.</para>
    ///
    ///     <para>Note that spreading an impulse over time does change the work it does, since the same impulse
    ///         applied at a different velocity transfers different kinetic energy. That is inherent to smoothing,
    ///         and the smoothed version is the more physical of the two anyway: it is monstermos emptying an
    ///         entire room in a single pass that is the unphysical part, not the ramp.</para>
    /// </remarks>
    /// <param name="frameTime">Seconds since the last call.</param>
    private void UpdateKsSpacingThrust(float frameTime)
    {
        if (_ksSpacingThrustAccumulators.Count == 0)
            return;

        // The reservoir is closed by removal, so it cannot be the thing we enumerate.
        _ksSpacingThrustGrids.Clear();
        _ksSpacingThrustGrids.AddRange(_ksSpacingThrustAccumulators.Keys);

        foreach (var gridUid in _ksSpacingThrustGrids)
        {
            KsDrainSpacingThrust(gridUid, frameTime);
        }
    }

    /// <summary>
    ///     Hands one grid its slice of the momentum it is owed. See <see cref="UpdateKsSpacingThrust"/>.
    /// </summary>
    /// <param name="gridUid">The grid to pay.</param>
    /// <param name="frameTime">Seconds since the last call.</param>
    private void KsDrainSpacingThrust(EntityUid gridUid, float frameTime)
    {
        ref var accumulator = ref CollectionsMarshal.GetValueRefOrNullRef(_ksSpacingThrustAccumulators, gridUid);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref accumulator)) // Importing CompilerServices here would make [Dependency] ambiguous.
            return;

        // Anchored and static grids are held in place by something other than their own inertia, and a map can opt
        // out of having its grids shoved around at all. Either way there is nobody to pay, so close the reservoir.
        if (!_ksPhysicsQuery.TryComp(gridUid, out var physicsComponent)
            || !_ksGridQuery.TryComp(gridUid, out var mapGridComponent)
            || !_ksTransformQuery.TryComp(gridUid, out var transformComponent)
            || physicsComponent.BodyType != BodyType.Dynamic)
        {
            _ksSpacingThrustAccumulators.Remove(gridUid);
            return;
        }

        var modifier = KsGetMapThrustModifier(transformComponent);
        if (modifier == 0f)
        {
            _ksSpacingThrustAccumulators.Remove(gridUid);
            return;
        }

        // How long this reservoir has gone unpaid, counting the frames below where the slice was too small to be
        // worth handing over. Draining against that rather than against frameTime is what keeps those frames from
        // slowing the ramp down - and what stops a reservoir too small to ever clear the threshold from stalling.
        accumulator.UnpaidTime += frameTime;

        // A tau of zero or less disables the ramp and pays each reservoir out the moment it is filled.
        var fraction = KsSpacingThrustSmoothing <= 0f
            ? 1f
            : 1f - MathF.Exp(-accumulator.UnpaidTime / KsSpacingThrustSmoothing);

        var releasedLinear = accumulator.LinearImpulse * fraction;
        var releasedAngular = accumulator.AngularImpulseAboutOrigin * fraction;

        var remainingLinear = accumulator.LinearImpulse - releasedLinear;
        var remainingAngular = accumulator.AngularImpulseAboutOrigin - releasedAngular;

        if (remainingLinear.LengthSquared() < KsMinimumSpacingImpulse * KsMinimumSpacingImpulse
            && MathF.Abs(remainingAngular) < KsMinimumSpacingImpulse)
        {
            // The dregs. Hand them over in one go so the reservoir actually closes instead of halving forever.
            releasedLinear += remainingLinear;
            releasedAngular += remainingAngular;
            _ksSpacingThrustAccumulators.Remove(gridUid);
        }
        else if (releasedLinear.LengthSquared() < KsMinimumSpacingImpulse * KsMinimumSpacingImpulse
                 && MathF.Abs(releasedAngular) < KsMinimumSpacingImpulse)
        {
            // Too small a slice to be worth waking a body for. Leave it in the reservoir and try again next frame
            // with a bigger one, since UnpaidTime - and with it the fraction - carries on growing.
            return;
        }
        else
        {
            accumulator.LinearImpulse = remainingLinear;
            accumulator.AngularImpulseAboutOrigin = remainingAngular;
            accumulator.UnpaidTime = 0f;
        }

        var linearImpulse = releasedLinear * modifier;

        // Shift the torque from the grid's origin to its centre of mass, which is what the body actually spins
        // about. Tile indices are in tiles, so scale them up to metres first.
        var angularImpulse = releasedAngular * mapGridComponent.TileSize * modifier
                             - Vector2Helpers.Cross(physicsComponent.LocalCenter, linearImpulse);

        // The reservoir is grid-local, the physics system wants world space.
        var worldRotation = _transformSystem.GetWorldRotation(transformComponent);

        _physics.ApplyLinearImpulse(gridUid, worldRotation.RotateVec(linearImpulse), body: physicsComponent);
        _physics.ApplyAngularImpulse(gridUid, angularImpulse, body: physicsComponent);
    }

    /// <summary>
    ///     The <see cref="MapAtmosphereComponent.KsGridThrustModifier"/> of the map a grid is on, or 1 if that map
    ///     has no atmosphere of its own. Zero for a grid that is not on a map at all.
    /// </summary>
    private float KsGetMapThrustModifier(TransformComponent transformComponent)
    {
        if (transformComponent.MapUid is not { } mapUid)
            return 0f;

        return _mapAtmosQuery.TryComp(mapUid, out var mapAtmosphereComponent)
            ? mapAtmosphereComponent.KsGridThrustModifier
            : 1f;
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
    ///     The momentum still owed to a grid for the gas it has lost (or gained). Filled by
    ///     <see cref="KsAccumulateSpacingThrust"/>, drained by <see cref="KsDrainSpacingThrust"/>.
    ///     Both members are in grid-local space.
    /// </summary>
    private struct KsSpacingThrustAccumulator
    {
        /// <summary>
        ///     Outstanding impulse, in newton-seconds.
        /// </summary>
        public Vector2 LinearImpulse;

        /// <summary>
        ///     Outstanding angular impulse about the grid's *origin* (not its centre of mass), in
        ///     newton-second-tiles.
        /// </summary>
        public float AngularImpulseAboutOrigin;

        /// <summary>
        ///     Seconds since this reservoir last paid anything out. The drain works against this rather than
        ///     against a single frame, so frames skipped for being too small to bother with are not lost time.
        /// </summary>
        public float UnpaidTime;
    }
}
