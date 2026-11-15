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
    public int Claim(Entity<KsMiningPointsSmelterComponent> smelter, Entity<KsMinerAccountComponent> account)
    {
        var points = (int)smelter.Comp.Unclaimed;
        if (points <= 0)
            return 0;

        smelter.Comp.Unclaimed -= points;
        Dirty(smelter);
        _salvageTabletSystem.AddPoints(account, points);
        return points;
    }

    /// <summary>
    ///     Claims with whatever the user offers: a tablet that is logged in, or an ID that has an account.
    /// </summary>
    private void ClaimWith(Entity<KsMiningPointsSmelterComponent> smelter, EntityUid user, EntityUid? used)
    {
        if (!TryFindAccount(user, used, out var account))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-smelter-no-account"), smelter.Owner, user);
            return;
        }

        // A claim that worked shows in the machine's window and the S.P.A.M.: only a claim of nothing is told.
        if (Claim(smelter, account) <= 0)
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-smelter-nothing"), smelter.Owner, user);
    }

    private bool TryFindAccount(EntityUid user, EntityUid? used, out Entity<KsMinerAccountComponent> account)
    {
        account = default;

        if (used is { } usedUid)
        {
            if (_tabletQuery.TryComp(usedUid, out var tabletComponent))
                return _salvageTabletSystem.TryGetAccount((usedUid, tabletComponent), out account);

            if (!_idCardSystem.TryGetIdCard(usedUid, out var usedCard) || !_accountQuery.TryComp(usedCard.Owner, out var usedAccountComponent))
                return false;

            account = (usedCard.Owner, usedAccountComponent);
            return true;
        }

        if (!_idCardSystem.TryFindIdCard(user, out var idCard) || !_accountQuery.TryComp(idCard.Owner, out var accountComponent))
            return false;

        account = (idCard.Owner, accountComponent);
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
        if (args.Handled || !_tabletQuery.HasComp(args.Used) && !_idCardSystem.TryGetIdCard(args.Used, out _))
            return;

        args.Handled = true;
        ClaimWith(entity, args.User, args.Used);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<KsMiningPointsSmelterComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || (int)entity.Comp.Unclaimed <= 0)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("ks-salvage-tablet-smelter-claim-verb", ("points", (int)entity.Comp.Unclaimed)),
            Act = () => ClaimWith(entity, user, used: null),
        });
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsMiningPointsSmelterComponent> entity, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange && (int)entity.Comp.Unclaimed > 0)
            args.PushMarkup(Loc.GetString("ks-salvage-tablet-smelter-examine", ("points", (int)entity.Comp.Unclaimed)));
    }
}
