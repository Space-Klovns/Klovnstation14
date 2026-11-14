using Content.Shared._KS14.DistressBeacon;
using Content.Shared.GameTicking;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.FieldCommander;

/// <summary>
///     Gives every defender a distress beacon on their neck when they spawn into a Field Commander round, so that
///         their side can find the ones who go down. Its own system rather than a part of the rule's, which already
///         has the subscription for a spawn.
/// </summary>
public sealed partial class KsFieldCommanderBeaconSystem : EntitySystem
{
    public static readonly EntProtoId BeaconPrototype = "KsDistressBeacon";


    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private EntityQuery<ScenarioFactionMemberComponent> _factionMemberQuery = default!;

    [SubscribeLocalEvent]
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!_factionMemberQuery.TryComp(args.Mob, out var factionMemberComponent))
            return;

        var query = EntityQueryEnumerator<ActiveGameRuleComponent, KsFieldCommanderRuleComponent>();
        while (query.MoveNext(out _, out _, out var ruleComponent))
        {
            if (factionMemberComponent.Id != ruleComponent.DefenderFaction)
                continue;

            GiveBeacon(args.Mob);
            return;
        }
    }

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

        Del(beaconUid);
        return false;
    }
}
