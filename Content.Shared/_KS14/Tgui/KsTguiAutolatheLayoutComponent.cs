using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Tgui;

/// <summary>
///     Marks a lathe whose tgui window is laid out as tg's Autolathe (a Total Materials section over the design browser)
///         rather than as its Fabricator (the browser over a material bar). Networked, because the client reads it off the
///         entity it opened the window for. Added to the autolathe by a partial prototype, so its variants inherit it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsTguiAutolatheLayoutComponent : Component;
