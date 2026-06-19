using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.SupplyPod.Trigger;

/// <summary>
///     Sends the trigger when the supply pod impacts the ground.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class TriggerOnSupplyPodLandedComponent : BaseTriggerOnXComponent;
