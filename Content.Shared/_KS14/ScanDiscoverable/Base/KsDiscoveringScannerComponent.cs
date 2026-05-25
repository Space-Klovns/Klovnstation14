using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ScanDiscoverable.Base;

/// <summary>
///     For items that can discover things via clicking on them.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsDiscoveringScannerComponent : Component;
