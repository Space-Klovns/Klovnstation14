using Robust.Shared.Audio;

namespace Content.Shared._Starlight.Plumbing.Components;

/// <summary>
///     A plumbing output that players can draw reagents from using containers.
///     Pulling from the network is handled by <see cref="PlumbingInletComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class PlumbingOutputComponent : Component
{
    /// <summary>
    ///     The name of the solution on this entity that players can draw from.
    /// </summary>
    [DataField]
    public string SolutionName = "output";

    /// <summary>
    ///     Sound played when reagents are drawn out.
    /// </summary>
    [DataField]
    public SoundSpecifier InteractSound = new SoundPathSpecifier("/Audio/Items/drink.ogg");
}
