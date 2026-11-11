using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Lava;

/// <summary>
///     Lava neither burns nor sinks this item. tgstation's <c>LAVA_PROOF</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsLavaProofComponent : Component;
