using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Movement.StopsDives;

/// <summary>
///     A dive that carries someone into this ends there: they come down in it, as at the end of any dive, rather than
///         sailing over. Barbed wire has it. Only dives (<see cref="Content.Shared.Movement.Components.ActiveLeaperComponent"/>):
///         something thrown still goes over. Needs a fixture a diver's body collides with; it need not be hard.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsStopsDivesComponent : Component;
