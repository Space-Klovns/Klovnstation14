using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Content.Shared.Trigger.Components.Effects;

namespace Content.Shared._KS14.Trigger.Components;

/// <summary>
/// KS14 - special boy demoncode so we can make a signal system for individual crit and death shit.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SignalRattleOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    /// When the entity goes crit we signal to this port.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> CritPort = "Crit";

    /// <summary>
    /// When the entity dies we signal to this port.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> DeathPort = "Dead";
}
