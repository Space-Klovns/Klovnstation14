namespace Content.Shared._KS14.Emissive;

/// <summary>
///     Turns an entity's <see cref="EmissiveLayersComponent"/> off while a boolean appearance value is false, such as a
///         computer's <c>enum.ComputerVisuals.Powered</c>. For layers that stay visible when they stop glowing, like an
///         unpowered computer's keyboard.
/// </summary>
[RegisterComponent]
public sealed partial class KsEmissiveAppearanceToggleComponent : Component
{
    /// <summary>
    ///     The appearance key. The layers glow while it's true or unset.
    /// </summary>
    [DataField(required: true)]
    public Enum Key = default!;
}
