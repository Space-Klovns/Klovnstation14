using Content.Shared.Roles.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Marks a mind role as belonging to a cultivator.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CultivatorRoleComponent : BaseMindRoleComponent;
