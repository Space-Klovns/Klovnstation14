using Content.Shared._KS14.DistressBeacon;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Gives a defender a distress beacon on their neck, so that their side can find the ones who go down. The rule
///         calls it as it readies each defender's body (<c>KsFieldCommanderRuleSystem.PrepareDefender</c>): as they
///         spawn, and for those already in the round when the rule starts.
/// </summary>
public sealed partial class KsFieldCommanderBeaconSystem : EntitySystem
{
    public static readonly EntProtoId BeaconPrototype = "KsDistressBeacon";

    [Dependency] private InventorySystem _inventorySystem = default!;

    /// <summary>
    ///     Puts a beacon on the entity's neck, if that slot is empty.
    /// </summary>
    public bool GiveBeacon(EntityUid uid)
    {
        if (!_inventorySystem.HasSlot(uid, KsDistressBeaconSystem.NeckSlot) ||
            _inventorySystem.TryGetSlotEntity(uid, KsDistressBeaconSystem.NeckSlot, out _))
            return false;

        var beaconUid = Spawn(BeaconPrototype, Transform(uid).Coordinates);
        if (_inventorySystem.TryEquip(uid, beaconUid, KsDistressBeaconSystem.NeckSlot, silent: true))
            return true;

        // Queued: this is called from inside a spawn, and from a sweep over every faction member.
        QueueDel(beaconUid);
        return false;
    }
}
