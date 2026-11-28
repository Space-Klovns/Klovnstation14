using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Emag.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.WallHealer;

public sealed partial class KsWallHealerSystem
{
    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<KsWallHealerComponent> healer, ref InteractUsingEvent args)
    {
        if (args.Handled
            || !_stackQuery.TryComp(args.Used, out var stackComponent)
            || stackComponent.StackTypeId != healer.Comp.SupplyStackType)
            return;

        args.Handled = true;

        var container = _containerSystem.EnsureContainer<Container>(healer, healer.Comp.SupplyContainerId);
        if (!_containerSystem.Insert(args.Used, container))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-restock-failed", ("item", args.Used), ("healer", healer.Owner)), healer, args.User);
            return;
        }

        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-wall-healer-restock", ("item", args.Used), ("healer", healer.Owner)),
            KsPopupMessage.Create("ks-wall-healer-restock-others", ("user", args.User), ("item", args.Used), ("healer", healer.Owner)),
            healer,
            args.User);
    }

    [SubscribeLocalEvent]
    private void OnGetAlternativeVerbs(Entity<KsWallHealerComponent> healer, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-wall-healer-verb-take-supply", ("item", GetLocalizedSupplyName(healer))),
            Act = () => TryTakeSupply(healer, user),
        });
    }

    /// <summary>
    ///     Hands <paramref name="user"/> one of the station's supplies.
    /// </summary>
    public bool TryTakeSupply(Entity<KsWallHealerComponent> healer, EntityUid user)
    {
        var itemName = GetSupplyName(healer);

        if (!_powerReceiverSystem.IsPowered(healer.Owner))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-supply-unpowered", ("item", itemName), ("healer", healer.Owner)), healer, user);
            return false;
        }

        if (!_containerSystem.TryGetContainer(healer, healer.Comp.SupplyContainerId, out var container)
            || container.ContainedEntities.Count == 0)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-supply-empty", ("item", itemName), ("healer", healer.Owner)), healer, user);
            return false;
        }

        if (_emagSystem.CheckFlag(healer, EmagType.Interaction))
        {
            var random = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
                (int)_gameTiming.CurTick.Value,
                KsSharedRandomExtensions.GetNetId(healer.Owner, EntityManager),
                KsSharedRandomExtensions.GetNetId(user, EntityManager));

            if (random.NextDouble() < healer.Comp.EmaggedSupplyJamChance)
            {
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-wall-healer-supply-jammed", ("item", itemName)), healer, user, type: PopupType.SmallCaution);
                return false;
            }
        }

        // The last one stocked comes out first. Whatever is left of its stack stays behind as a new entity, so the one
        // handed over is one that already exists and the pickup can be predicted.
        var item = container.ContainedEntities[^1];
        if (_stackQuery.TryComp(item, out var stackComponent) && stackComponent.Count > 1)
        {
            var remainderCount = stackComponent.Count - 1;
            _stackSystem.SetCount((item, stackComponent), 1);

            var remainder = PredictedSpawnInContainerOrDrop(ProtoMan.Index(stackComponent.StackTypeId).Spawn, healer, healer.Comp.SupplyContainerId);
            _stackSystem.SetCount(remainder, remainderCount);
        }

        _containerSystem.Remove(item, container);
        _handsSystem.PickupOrDrop(user, item);

        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-wall-healer-supply-taken", ("item", item), ("healer", healer.Owner)),
            KsPopupMessage.Create("ks-wall-healer-supply-taken-others", ("user", user), ("item", item), ("healer", healer.Owner)),
            healer,
            user);
        _audioSystem.PlayPredicted(healer.Comp.SupplySound, healer, user);
        return true;
    }

    /// <summary>
    ///     How many supplies the station holds, counting every item in its stacks.
    /// </summary>
    public int GetSupplyCount(Entity<KsWallHealerComponent> healer)
    {
        if (!_containerSystem.TryGetContainer(healer, healer.Comp.SupplyContainerId, out var container))
            return 0;

        var count = 0;
        foreach (var item in container.ContainedEntities)
            count += _stackSystem.GetCount(item);

        return count;
    }

    /// <summary>
    ///     The supplies' name, for a popup, which names it in each recipient's language.
    /// </summary>
    private KsPopupPrototypeName GetSupplyName(Entity<KsWallHealerComponent> healer)
    {
        return new KsPopupPrototypeName(ProtoMan.Index(healer.Comp.SupplyStackType).Spawn);
    }

    /// <summary>
    ///     The supplies' name in the active culture, for examine and verb text.
    /// </summary>
    private string GetLocalizedSupplyName(Entity<KsWallHealerComponent> healer)
    {
        var prototype = ProtoMan.Index<EntityPrototype>(ProtoMan.Index(healer.Comp.SupplyStackType).Spawn);
        return _contentLocalizationManager.GetLocalizedPrototypeName(prototype);
    }
}
