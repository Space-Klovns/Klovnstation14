using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Carrying;

/// <summary>
///     Can hoist someone lying on the floor onto their back, by alt-clicking them or dragging them onto themselves.
///     tgstation's fireman carry, without its need for an aggressive grab.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsFiremanCarrierComponent : Component
{
    /// <summary>
    ///     How long lifting someone takes.
    /// </summary>
    [DataField]
    public TimeSpan CarryTime = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     How long someone being carried needs to wriggle free.
    /// </summary>
    [DataField]
    public TimeSpan EscapeTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     How many of the carrier's hands a carry takes, each of which must be empty or holding the one lifted.
    ///     tgstation's fireman carry needs both arms.
    /// </summary>
    [DataField]
    public int HandsNeeded = 2;

    /// <summary>
    ///     Speed multiplier of the carrier while carrying.
    /// </summary>
    [DataField]
    public float SpeedModifier = 0.8f;

    [DataField]
    public ProtoId<AlertPrototype> CarryingAlert = "KsCarrying";

    [DataField]
    public ProtoId<AlertPrototype> CarriedAlert = "KsCarried";
}
