using Content.Shared._KS14.Atmos.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Popups;
using Content.Shared.Wires;

namespace Content.Shared._KS14.Atmos.EntitySystems;

public abstract partial class SharedGasPistonSystem : EntitySystem
{
    [Dependency] protected readonly SharedPopupSystem PopupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GasPistonComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
        SubscribeLocalEvent<GasPistonComponent, AttemptChangePanelEvent>(OnAttemptChangePanel);
    }

    private void OnUnanchorAttempt(Entity<GasPistonComponent> entity, ref UnanchorAttemptEvent args)
    {
        // Cancel unanchor if piston is extended or something

        if (args.Cancelled ||
            !entity.Comp.Extended)
            return;

        args.Cancel();
        PopupSystem.PopupPredicted(Loc.GetString("gas-piston-popup-retractfirst"), entity.Owner, args.User);
    }

    private void OnAttemptChangePanel(Entity<GasPistonComponent> entity, ref AttemptChangePanelEvent args)
    {
        // Don't let wirepanel be opened/closed if piston is extended (to prevent construction or something)

        if (args.Cancelled ||
            !entity.Comp.Extended)
            return;

        args.Cancelled = true;

        if (args.User is { } userUid)
            PopupSystem.PopupPredicted(Loc.GetString("gas-piston-popup-retractfirst"), entity.Owner, userUid);
    }
}
