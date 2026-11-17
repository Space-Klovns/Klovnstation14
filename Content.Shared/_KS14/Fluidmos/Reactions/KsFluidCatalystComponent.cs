using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Fluidmos.Reactions;

/// <summary>
///     A catalyst bed, which goes in a catalytic reactor's slot.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsFluidCatalystSystem))]
public sealed partial class KsFluidCatalystComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KsFluidCatalystPrototype> Catalyst;

    /// <summary>
    ///     How much of a fresh bed's activity is left, out of 1. Poisons wear it down, and a spent bed does nothing.
    /// </summary>
    /// <seealso cref="KsFluidCatalystSystem.Poison"/>
    [DataField, AutoNetworkedField]
    public float Activity = 1f;
}
