namespace Content.Client._KS14.Power.PTL;

[RegisterComponent]
public sealed partial class PtlVisualsComponent : Component
{
    [DataField]
    public string ChargePrefix = "charge-";

    [DataField]
    public int MaxChargeStates = 6;
}
