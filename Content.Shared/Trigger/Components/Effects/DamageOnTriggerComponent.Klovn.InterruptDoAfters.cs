namespace Content.Shared.Trigger.Components.Effects;

public sealed partial class DamageOnTriggerComponent
{
    [DataField, AutoNetworkedField]
    public bool InterruptDoAfters = true;
}
