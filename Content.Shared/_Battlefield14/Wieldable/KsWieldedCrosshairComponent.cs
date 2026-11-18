using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._Battlefield14.Wieldable;

/// <summary>
///     A gun's own crosshair: drawn at the cursor, in place of the combat-mode sight, while the gun is held wielded in
///         the active hand in combat mode. The system cursor is hidden meanwhile.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsWieldedCrosshairComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public SpriteSpecifier.Rsi Crosshair = default!;
}
