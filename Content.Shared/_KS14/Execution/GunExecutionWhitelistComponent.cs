using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Execution;

/// <summary>
/// Used to whitelist guns that can be used for executions.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GunExecutionWhitelistComponent : Component;
