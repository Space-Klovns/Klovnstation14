using Content.Shared.Roles.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Anchorless.Components;

/// <summary>
/// Marks a mind role as belonging to an Anchorless antagonist.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AnchorlessRoleComponent : BaseMindRoleComponent;