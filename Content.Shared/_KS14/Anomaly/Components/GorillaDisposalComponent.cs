using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Anomaly.Components;

/// <summary>
/// A tag component that allows an entity to be shoved into a disposal unit by the G.O.R.I.L.L.A. gauntlet.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GorillaDisposalComponent : Component;
