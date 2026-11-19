// BF14-BLU: ported from Monolith via Battlefield14 and reworked, see ArmorPlateSystem
using Robust.Shared.GameStates;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
///     Clothing whose storage holds armor plates. The first plate put in is the active one: it absorbs the wearer's
///         damage and sets their speed, until it breaks or is taken out, when the next plate in storage takes over.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ArmorPlateSystem))]
public sealed partial class ArmorPlateHolderComponent : Component
{
    /// <summary>
    ///     The plate in use, one of those in storage.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ActivePlate;

    /// <summary>
    ///     Whether the wearer is told when the active plate breaks.
    /// </summary>
    [DataField]
    public bool ShowBreakPopup = true;
}
