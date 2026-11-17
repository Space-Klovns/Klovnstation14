using Content.Shared._KS14.Materials;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Verbs;

namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     Ore put into a smelter earns mining points, which wait on the machine for a salvager to claim. Points only:
///         reputation comes from tasks.
/// </summary>
public sealed partial class KsMiningPointsSmelterSystem : EntitySystem
{
    [Dependency] private KsSalvageTabletSystem _salvageTabletSystem = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;
    [Dependency] private EntityQuery<KsSalvageTabletComponent> _tabletQuery = default!;
    [Dependency] private EntityQuery<KsMinerAccountComponent> _accountQuery = default!;

    /// <summary>
    ///     Moves the machine's unclaimed points to an account.
    /// </summary>
    /// <returns>How many points were claimed.</returns>
    public int Claim(Entity<KsMiningPointsSmelterComponent> smelterEntity, Entity<KsMinerAccountComponent> accountEntity)
    {
        var points = (int)smelterEntity.Comp.Unclaimed;
        if (points <= 0)
            return 0;

        smelterEntity.Comp.Unclaimed -= points;
        Dirty(smelterEntity);
        _salvageTabletSystem.AddPoints(accountEntity, points);
        return points;
    }

    /// <summary>
    ///     Claims with whatever the user offers: a tablet that is logged in, or an ID that has an account.
    /// </summary>
    private void ClaimWith(Entity<KsMiningPointsSmelterComponent> smelterEntity, EntityUid userUid, EntityUid? usedUid)
    {
        if (!TryFindAccount(userUid, usedUid, out var accountEntity))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-smelter-no-account"), smelterEntity.Owner, userUid);
            return;
        }

        // A claim that worked shows in the machine's window and the S.P.A.M.: only a claim of nothing is told.
        if (Claim(smelterEntity, accountEntity) <= 0)
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-smelter-nothing"), smelterEntity.Owner, userUid);
    }

    private bool TryFindAccount(EntityUid userUid, EntityUid? usedUid, out Entity<KsMinerAccountComponent> accountEntity)
    {
        accountEntity = default;

        if (usedUid is { } offeredUid)
        {
            if (_tabletQuery.TryComp(offeredUid, out var tabletComponent))
                return _salvageTabletSystem.TryGetAccount((offeredUid, tabletComponent), out accountEntity);

            if (!_idCardSystem.TryGetIdCard(offeredUid, out var usedCardEntity) || !_accountQuery.TryComp(usedCardEntity.Owner, out var usedAccountComponent))
                return false;

            accountEntity = (usedCardEntity.Owner, usedAccountComponent);
            return true;
        }

        if (!_idCardSystem.TryFindIdCard(userUid, out var idCardEntity) || !_accountQuery.TryComp(idCardEntity.Owner, out var accountComponent))
            return false;

        accountEntity = (idCardEntity.Owner, accountComponent);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnMaterialInserted(Entity<KsMiningPointsSmelterComponent> entity, ref KsMaterialEntityInsertedEvent args)
    {
        if (!_stackQuery.TryComp(args.Inserted, out var stackComponent) ||
            !entity.Comp.PointsPerUnit.TryGetValue(stackComponent.StackTypeId, out var pointsPerUnit))
            return;

        entity.Comp.Unclaimed += pointsPerUnit * args.Units;
        Dirty(entity);
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<KsMiningPointsSmelterComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || (!_tabletQuery.HasComp(args.Used) && !_idCardSystem.TryGetIdCard(args.Used, out _)))
            return;

        args.Handled = true;
        ClaimWith(entity, args.User, args.Used);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<KsMiningPointsSmelterComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || (int)entity.Comp.Unclaimed <= 0)
            return;

        var userUid = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-salvage-tablet-smelter-claim-verb", ("points", (int)entity.Comp.Unclaimed)),
            Act = () => ClaimWith(entity, userUid, usedUid: null),
        });
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsMiningPointsSmelterComponent> entity, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange && (int)entity.Comp.Unclaimed > 0)
            args.PushMarkup(Loc.GetString("ks-salvage-tablet-smelter-examine", ("points", (int)entity.Comp.Unclaimed)));
    }
}
