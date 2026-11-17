using Content.Shared.Inventory;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Lawnmower;

[RegisterComponent, NetworkedComponent]
public sealed partial class KsGrillingShortsComponent : Component
{
    /// <summary>
    ///     Multiplies the speed of a mower its wearer rides.
    /// </summary>
    [DataField]
    public float MowerSpeedMultiplier = 1.1f;

    /// <summary>
    ///     The slots the shorts work from.
    /// </summary>
    [DataField]
    public SlotFlags Slots = SlotFlags.INNERCLOTHING;
}
