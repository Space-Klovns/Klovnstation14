using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Fluids.Components;

/// <summary>
///     Added to a puddle with every instance of it removed at the end of an atmos tick when
///         the speed of evaporation is being changed by some atmos reaction.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class AtmosEvaporatingPuddleComponent : Component
{
    /// <summary>
    ///     Highest amount of evaporation possible or something
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 EvaporationAmount;

    [DataField(serverOnly: true)]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(1d);
}
