using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.ArcFlash.Components;

public abstract partial class BaseArcFlashImpactComponent : Component
{
    [DataField(readOnly: true, serverOnly: true)]
    public float LightningRange = 3f;

    [DataField(readOnly: true, serverOnly: true)]
    public int LightningAmount = 1;

    [DataField(readOnly: true, serverOnly: true)]
    public string LightningPrototype = "ArcFlashLightningWeak";

    /// <summary>
    ///     The chemfire an arc flash starts on whatever emitted it, and on every electrical thing its bolts hit. Null
    ///         starts none.
    /// </summary>
    [DataField(readOnly: true, serverOnly: true)]
    public EntProtoId? ElectricalFirePrototype = "ChemicalFireElectrical";
}
