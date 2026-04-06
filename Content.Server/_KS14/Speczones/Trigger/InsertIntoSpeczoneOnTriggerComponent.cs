using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Speczones.Trigger;

[RegisterComponent]
public sealed partial class InsertIntoSpeczoneOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     Speczone to insert into. Can be null.
    /// </summary>
    [DataField]
    public ProtoId<SpeczonePrototype>? SpeczoneId = null;
}
