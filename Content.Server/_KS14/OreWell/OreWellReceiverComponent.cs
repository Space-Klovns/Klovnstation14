namespace Content.Server._KS14.OreWell;

[RegisterComponent]
public sealed partial class OreWellReceiverComponent : Component
{
    [DataField]
    public object? FlickLayerKey = null;

    [DataField]
    public string FlickState = "";
}
