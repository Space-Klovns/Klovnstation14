using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Speczones;

/// <summary>
///     Added to things whose use gets blocked when in speczones,
///         so as to allow them to always be used in speczones.
///
///     Intended to be manually added by admins.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AlwaysAllowedInSpeczoneComponent : Component;
