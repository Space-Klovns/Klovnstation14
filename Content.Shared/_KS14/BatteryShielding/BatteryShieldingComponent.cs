using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.BatteryShielding;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedBatteryShieldingSystem))]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class BatteryShieldingComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = false;

    /// <summary>
    ///     Multiplier for charge used per second.
    /// </summary>
    [DataField]
    public float ChargeUseRateMultiplier = 300f;

    [DataField(serverOnly: true)]
    public bool RaiseAdminLogs = true;

    [AutoNetworkedField]
    public float DischargeRate = 0f;

    /// <summary>
    ///     Key of a UI that will or will not exist on the entity, which will
    ///         have its UI updated when necessary.
    ///
    ///     If null, this does not happen.
    /// </summary>
    [DataField]
    public Enum? UiKey = null;

    /// <summary>
    ///     Popup for when the shield is about to BTFO everything nearby after being emagged.
    /// </summary>
    [DataField]
    public LocId? EmagMalfunctionPopupLoc = null;

    /// <summary>
    ///     Sound for when the shield is about to BTFO everything nearby after being emagged.
    /// </summary>
    [DataField]
    public SoundSpecifier? EmagMalfunctionSound = new SoundPathSpecifier(
        "/Audio/Effects/metal_scrape1.ogg",
        AudioParams.Default
    );

    [DataField]
    public TimeSpan EmagMalfunctionDuration = TimeSpan.Zero;

    /// <summary>
    ///     The sound played when the shield BTFOs everything after being emagged.
    /// </summary>
    [DataField]
    public SoundSpecifier? EmagImplosionSound = new SoundPathSpecifier(
        "/Audio/Effects/singularity_collapse.ogg",
        AudioParams.Default
    );

    /// <summary>
    ///     Popup for when the shield is under load.
    /// </summary>
    [DataField]
    public LocId? FalterPopupLoc = null;

    /// <summary>
    ///     Popup for when the shield finally fully fails under load.
    /// </summary>
    [DataField]
    public LocId? FailPopupLoc = null;
}

[Serializable, NetSerializable]
public enum BatteryShieldingUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum BatteryShieldingVisuals : byte
{
    /// <summary>
    ///     <see langword="bool"/>
    /// </summary>
    Active
}

[Serializable, NetSerializable]
public sealed class BatteryShieldingToggleMessage : BoundUserInterfaceMessage;
