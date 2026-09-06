namespace Content.Shared._KS14.ArcFlash.Components;

public abstract partial class BaseArcFlashImpactComponent : Component
{
    [DataField, AutoNetworkedField]
    public float LightningRange = 3f;

    [DataField, AutoNetworkedField]
    public int LightningAmount = 1;

    [DataField, AutoNetworkedField]
    public string LightningPrototype = "ArcFlashLightningWeak";
}
