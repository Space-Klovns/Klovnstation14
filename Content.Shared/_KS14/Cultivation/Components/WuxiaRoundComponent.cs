using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Marks a station as being in a wuxia round, so cultivation things that are otherwise gamemode-only (crafting wuxia
///         weapons, for one) are open on it. Added to every station when the wuxia rule starts.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WuxiaRoundComponent : Component;
