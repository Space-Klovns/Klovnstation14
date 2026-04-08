using Robust.Shared.Utility;

namespace Content.Server._KS14.Ordnance.TTV;

/// <summary>Component for items that are compatible in a TTV.</summary>
[RegisterComponent, ComponentProtoName("ttvCompatible")]
public sealed partial class TTVCompatibleComponent : Component
{
    /// <summary>The texture path of this item when it's inserted into a TTV.</summary>
    [DataField("sprite")]
    public ResPath? InsertedTexture = new("_Goobstation/Objects/Weapons/Bombs/ttv.rsi");

    /// <summary>The texture state of this item when it's inserted into a TTV.</summary>
    [DataField("state")]
    public string InsertedState = "generic";
}
