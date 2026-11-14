using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Bane;

/// <summary>
///     Marks lavaland and asteroid fauna, megafauna included, for weapons made to kill them.
///         tgstation's <c>MOB_MINING</c> biotype.
/// </summary>
/// <seealso cref="KsBaneComponent"/>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsMiningFaunaComponent : Component;
