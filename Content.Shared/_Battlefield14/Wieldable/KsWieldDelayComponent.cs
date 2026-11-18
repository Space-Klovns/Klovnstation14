using Robust.Shared.GameStates;

namespace Content.Shared._Battlefield14.Wieldable;

/// <summary>
///     A heavy weapon that takes time to get ready: after it is picked up it can't be wielded until
///         <see cref="ModifiedDelay"/> has passed, and after it is wielded, with <see cref="PreventFiring"/>, it can't fire
///         until the same delay has passed again.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsWieldDelaySystem))]
public sealed partial class KsWieldDelayComponent : Component
{
    /// <summary>
    ///     The delay before anything modifies it (<see cref="KsGetWieldDelayEvent"/>).
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan BaseDelay = TimeSpan.FromSeconds(0.4);

    /// <summary>
    ///     The delay in use: <see cref="BaseDelay"/> as modified, set by <see cref="KsWieldDelaySystem.RefreshWieldDelay"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public TimeSpan ModifiedDelay = TimeSpan.FromSeconds(0.4);

    /// <summary>
    ///     Whether the gun can't fire until the delay after wielding it has passed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool PreventFiring;
}

/// <summary>
///     Raised on a <see cref="KsWieldDelayComponent"/> item to work out its wield delay, starting from
///         <see cref="KsWieldDelayComponent.BaseDelay"/>; an attachment adds to or scales <see cref="Delay"/>. Pure:
///         handlers change only the event.
/// </summary>
[ByRefEvent]
public record struct KsGetWieldDelayEvent(TimeSpan Delay);
