using Robust.Shared.GameStates;

namespace Content.Shared._KS14.SupplyPod;

[Access(typeof(SharedSupplyPodSystem))]
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class SupplyPodComponent : Component
{
    /// <summary>
    ///     Is it open, if theres a door?
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Open = false;
}

[Access(typeof(SharedSupplyPodSystem))]
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class SupplyPodDoorDrawerComponent : Component
{
    [DataField, AutoNetworkedField]
    public Angle Rotation = Angle.Zero;

    /// <summary>
    ///     Must point to an RSI, not raw texture.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public PrototypeLayerData DoorData;

    /// <summary>
    ///     Must point to an RSI, not raw texture.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public PrototypeLayerData? DecalData;
}
