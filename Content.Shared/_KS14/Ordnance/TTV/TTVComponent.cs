using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Ordnance.TTV;

/// <summary>A tank-transfer valve that can hold multiple itemslots.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TTVComponent : Component
{
    /// <summary>Whether this TTV is blowing up.</summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool Igniting = false;

    /// <summary>Whether this TTV should be mixing gas.</summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool Open = false;

    /// <summary>Sound when opening or closing the TTV.</summary>
    [DataField]
    public SoundSpecifier ToggleSound = new SoundCollectionSpecifier("valveSqueak");

    /// <summary>Map key used for this TTV when displaying tanks on it, while being worn.</summary>
    [DataField]
    public string ClothingMapKey = "ttv";
}

public enum TTVLayers : byte
{
    Valve,
}
