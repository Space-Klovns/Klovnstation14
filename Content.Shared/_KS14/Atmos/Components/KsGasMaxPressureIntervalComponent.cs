using Content.Shared._KS14.Atmos.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Atmos.Components;

/// <summary>
///     Caps maxcap integrity loss to intervals instead of once every atmos update.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsGasMaxPressureIntervalSystem))]
public sealed partial class KsGasMaxPressureIntervalComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan Interval = TimeSpan.Zero;

    /// <summary>
    ///     Next time that this is allowed to lose integrity in game-time.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextUpdate = TimeSpan.MinValue;

    /// <summary>
    ///     Popups to display for every integrity level.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public SortedDictionary<float, LocId?> PopupLocs = [];

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public PopupType PopupType = PopupType.Small;
}
