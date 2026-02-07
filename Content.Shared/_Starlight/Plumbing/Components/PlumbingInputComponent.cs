using Robust.Shared.Audio;

namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     A plumbing input that players can pour reagents into using containers.
///     Paired with <see cref="PlumbingOutletComponent"/> to allow other machines
///     to pull reagents from its solution via the plumbing network.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingInputComponent : Component
{
    /// <summary>
    ///     The name of the solution on this entity that receives input.
    /// </summary>
    [DataField]
    public string SolutionName = "input";

    /// <summary>
    ///     Sound played when reagents are poured in.
    /// </summary>
    [DataField]
    public SoundSpecifier InteractSound = new SoundPathSpecifier("/Audio/Items/drink.ogg");
}
