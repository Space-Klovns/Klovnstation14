using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ScanDiscoverable.Base;

/// <summary>
///     The name something was discovered under, as a locale key. An entity's own name is one string, in the server's
///         language, so each client reads this to show the name in its own.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(KsScanDiscoverableSystem))]
public sealed partial class KsDiscoveredNameComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public LocId Name;
}
