using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Shared._KS14.ScanDiscoverable.Base;

public sealed class KsScanDiscoverableSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaDataSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsScanDiscoverableComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<KsScanDiscoverableComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnExamined(Entity<KsScanDiscoverableComponent> entity, ref ExaminedEvent args)
    {
        if (entity.Comp.ExamineLoc is not { } examineLoc)
            return;

        args.PushMarkup(Loc.GetString(examineLoc), priority: 3);
    }

    private void OnInteractUsing(Entity<KsScanDiscoverableComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            !HasComp<KsDiscoveringScannerComponent>(args.Used))
            return;

        _metaDataSystem.SetEntityName(entity.Owner, entity.Comp.TrueName);
        if (entity.Comp.DiscoveryPopupLoc is { } popupLoc)
        {
            _popupSystem.PopupPredicted(
                Loc.GetString(popupLoc, ("name", entity.Comp.TrueName)),
                entity,
                args.User,
                Filter.PvsExcept(args.User),
                true
            );
        }

        var ev = new KsAfterScanDiscoveringEvent(entity.Comp.TrueName, args);
        RaiseLocalEvent(entity, ref ev);
        RaiseLocalEvent(args.Used, ref ev);

        args.Handled = true;
        RemComp(entity, entity.Comp);
    }
}

/// <summary>
///     After something is discovered, raised on the thing that was discovered and what was used to discover it.
/// </summary>
[ByRefEvent]
public record struct KsAfterScanDiscoveringEvent(string TrueName, InteractUsingEvent InteractUsingEvent);
