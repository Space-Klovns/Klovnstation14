namespace Content.Shared._KS14.ArcFlash.Components;

public abstract partial class BaseArcFlashImpactComponent : Component
{
    [DataField(readOnly: true, serverOnly: true)]
    public float LightningRange = 3f;

    [DataField(readOnly: true, serverOnly: true)]
    public int LightningAmount = 1;

    [DataField(readOnly: true, serverOnly: true)]
    public string LightningPrototype = "ArcFlashLightningWeak";
}
