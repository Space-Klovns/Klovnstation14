using Content.Shared.Atmos.Components;

namespace Content.Shared.Atmos.EntitySystems;

/// <summary>
///     KS14: the air of a map, for both sides. Atmos gives a map's air (<see cref="MapAtmosphereComponent"/>) to everything
///         on it that it does not simulate: what is off every grid, and a grid's tiles with no gas of their own, such as
///         those with no floor. A map with no atmosphere at all is space.
/// </summary>
/// <remarks>
///     On the server, a tile known by its grid and indices goes through <c>AtmosphereSystem.GetGridTileMixture</c> and
///         <c>IsGridTileSpace</c>, which ask the grid first and come here for the rest. The client simulates no gas, so
///         this is all it can know.
/// </remarks>
public abstract partial class SharedAtmosphereSystem
{
    [Dependency] private EntityQuery<MapAtmosphereComponent> _ksMapAtmosphereQuery = default!;

    /// <summary>
    ///     The air of <paramref name="mapUid"/>, or of space if it has none. Immutable either way.
    /// </summary>
    public GasMixture GetMapMixture(EntityUid? mapUid)
    {
        return mapUid is { } uid && _ksMapAtmosphereQuery.TryComp(uid, out var mapAtmosphereComponent)
            ? mapAtmosphereComponent.Mixture
            : GasMixture.SpaceGas;
    }

    /// <summary>
    ///     Whether what <paramref name="mapUid"/> has outside is space: true unless it has an atmosphere that says otherwise,
    ///         as a planet's does.
    /// </summary>
    public bool IsMapSpace(EntityUid? mapUid)
    {
        return mapUid is not { } uid || !_ksMapAtmosphereQuery.TryComp(uid, out var mapAtmosphereComponent) || mapAtmosphereComponent.Space;
    }

    /// <summary>
    ///     Puts the map's air into its state for the client, which would otherwise only ever see space.
    /// </summary>
    protected static void KsWriteMapAir(MapAtmosphereComponent component, MapAtmosphereComponentState state)
    {
        var mixture = component.Mixture;
        var moles = new float[Atmospherics.AdjustedNumberOfGases];

        for (var i = 0; i < moles.Length; i++)
        {
            moles[i] = mixture.GetMoles(i);
        }

        state.Space = component.Space;
        state.Moles = moles;
        state.Temperature = mixture.Temperature;
        state.Volume = mixture.Volume;
    }

    /// <summary>
    ///     Takes the map's air from its state. See <see cref="KsWriteMapAir"/>.
    /// </summary>
    protected static void KsReadMapAir(MapAtmosphereComponent component, MapAtmosphereComponentState state)
    {
        component.Space = state.Space;

        if (state.Moles is not { Length: Atmospherics.AdjustedNumberOfGases } moles)
            return;

        var mixture = new GasMixture((float[]) moles.Clone(), state.Temperature, state.Volume);
        mixture.MarkImmutable();
        component.Mixture = mixture;
    }
}
