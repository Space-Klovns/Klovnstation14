using Content.Shared.Trigger.Components.Triggers;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ScanDiscoverable.Trigger;

/// <summary>
///     Does a trigger upon getting discovered.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsScanTriggerOnDiscoveredComponent : BaseTriggerOnXComponent;
