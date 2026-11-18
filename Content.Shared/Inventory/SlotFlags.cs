using Robust.Shared.Serialization;

namespace Content.Shared.Inventory;

/// <summary>
///     Defines what slot types an item can fit into.
/// </summary>
[Serializable, NetSerializable]
[Flags]
public enum SlotFlags
{
    NONE = 0,
    PREVENTEQUIP = 1 << 0,
    HEAD = 1 << 1,
    EYES = 1 << 2,
    EARS = 1 << 3,
    MASK = 1 << 4,
    OUTERCLOTHING = 1 << 5,
    INNERCLOTHING = 1 << 6,
    NECK = 1 << 7,
    BACK = 1 << 8,
    BELT = 1 << 9,
    GLOVES = 1 << 10,
    IDCARD = 1 << 11,
    POCKET = 1 << 12,
    LEGS = 1 << 13,
    FEET = 1 << 14,
    SUITSTORAGE = 1 << 15,
    // BF14-BLU start: Mono and BF14 slots. BF14 had Frontier's WALLET at 1 << 16, which is not ported
    BALACLAVA = 1 << 16,
    ARMBANDRIGHT = 1 << 17,
    ARMBANDLEFT = 1 << 18,
    HELMETCOVER = 1 << 19,
    HELMETATTACHMENT = 1 << 20,
    SUITSTORAGE2 = 1 << 21,
    WEBBING = 1 << 22,
    // BF14-BLU end
    All = ~NONE,

    WITHOUT_POCKET = All & ~POCKET
}
