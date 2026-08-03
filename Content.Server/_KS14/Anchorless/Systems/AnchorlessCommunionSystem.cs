using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Server._KS14.Anchorless.Systems;

public sealed class AnchorlessCommunionSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private AnchorlessIdentitySystem _anchorlessIdentity = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnchorlessCommunionActionEvent>(OnCommunionAction);
    }

    private void OnCommunionAction(AnchorlessCommunionActionEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Target == args.Performer)
            return;

        if (!TryComp<AnchorlessIdentityComponent>(args.Performer, out var performerIdentity) ||
            !TryComp<AnchorlessIdentityComponent>(args.Target, out var targetIdentity))
            return;

        _anchorlessIdentity.MergeIdentities((args.Performer, performerIdentity), (args.Target, targetIdentity));

        _popup.PopupClient(Loc.GetString("anchorless-communion-message"), args.Performer, PopupType.Medium);
        _popup.PopupClient(Loc.GetString("anchorless-communion-message"), args.Target, PopupType.Medium);
    }
}
