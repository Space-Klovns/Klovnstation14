namespace Content.Shared.Payload.Components;

public sealed partial class ChemicalPayloadComponent : Component
{
    /// <summary>
    ///     Should the contents of this spill on the ground after
    ///         everything mixes and reacts once?
    /// </summary>
    [DataField]
    public bool Spill = false;
}
