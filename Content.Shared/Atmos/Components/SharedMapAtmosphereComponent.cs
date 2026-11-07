using Content.Shared.Atmos.EntitySystems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Atmos.Components;

[NetworkedComponent]
public abstract partial class SharedMapAtmosphereComponent : Component
{
    [ViewVariables] public SharedGasTileOverlaySystem.GasOverlayData OverlayData;
}

[Serializable, NetSerializable]
public sealed class MapAtmosphereComponentState : ComponentState
{
    public SharedGasTileOverlaySystem.GasOverlayData Overlay;

    // KS14 start: the map's air, so the client knows what is outside too. See SharedAtmosphereSystem.GetMapMixture.
    //      GasMixture is not net-serializable, so it comes as its parts.
    public bool Space = true;
    public float[]? Moles;
    public float Temperature;
    public float Volume;
    // KS14 end

    public MapAtmosphereComponentState(SharedGasTileOverlaySystem.GasOverlayData overlay)
    {
        Overlay = overlay;
    }
}
