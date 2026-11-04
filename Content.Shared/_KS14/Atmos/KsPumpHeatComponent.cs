using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Atmos;

/// <summary>
///     Where a working pump's compression heat is going, and the extra power that is costing it. On a device only while
///         it is compressing something or still drawing more than it does at idle, so an idle pump pays nothing.
/// </summary>
/// <remarks>
///     Powers are smoothed, and on the same scale as a device's electrical load, so that <see cref="GasPower"/> plus
///         <see cref="RoomPower"/> is what the device is billed for.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsSharedPumpHeatSystem))]
public sealed partial class KsPumpHeatComponent : Component
{
    /// <summary>
    ///     Watts of compression heat staying in the moved gas.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float GasPower;

    /// <summary>
    ///     Watts of compression heat going into the air around the device.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float RoomPower;

    /// <summary>
    ///     Watts of compression heat turned back into electricity, which the device isn't billed for.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float RecoveredPower;

    /// <summary>
    ///     The load the device had before it started working. Saved, because the raised load is too: without this, a map
    ///         saved mid-load would come back with the raised load as its idle.
    /// </summary>
    [DataField]
    public float IdleLoad;

    /// <summary>
    ///     Joules into the gas, unscaled, since the last sample.
    /// </summary>
    [ViewVariables]
    public float PendingGasHeat;

    /// <summary>
    ///     Joules into the room, unscaled, since the last sample.
    /// </summary>
    [ViewVariables]
    public float PendingRoomHeat;

    /// <summary>
    ///     Joules recovered, unscaled, since the last sample.
    /// </summary>
    [ViewVariables]
    public float PendingRecoveredHeat;

    [DataField, AutoPausedField]
    public TimeSpan LastSample;

    [DataField, AutoPausedField]
    public TimeSpan NextSample;

    /// <summary>
    ///     Until this time, the device stays at its idle load, after losing power.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan HoldIdleUntil;
}
