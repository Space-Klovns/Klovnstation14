using Content.Server.Antag;
using Content.Server._KS14.GameTicking.Rules.Components;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._KS14.Anchorless.Systems;

/// <summary>Server-authoritative recruitment. Role assignment and mind transfer cannot be predicted.</summary>
public sealed partial class AnchorlessConversionSystem : EntitySystem
{
    [Dependency] private AntagSelectionSystem _antags = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AnchorlessComponent, AnchorlessConvertActionEvent>(OnConvert);
    }

    private void OnConvert(Entity<AnchorlessComponent> ent, ref AnchorlessConvertActionEvent args)
    {
        if (args.Handled || HasComp<AnchorlessComponent>(args.Target) ||
            (!_mobState.IsCritical(args.Target) && !_mobState.IsDead(args.Target)) ||
            !_mind.TryGetMind(args.Target, out _, out var mind) ||
            !_players.TryGetSessionById(mind.UserId, out var session))
            return;

        args.Handled = true;
        // The mind is already attached to this body. Transferring it to the same entity
        // can fail for dead/ghosted players and prevented the antag assignment entirely.
        _damage.ClearAllDamage(args.Target);
        _mobState.ChangeMobState(args.Target, MobState.Alive);
        _antags.ForceMakeAntag<AnchorlessRuleComponent>(session, "Anchorless");
        RaiseLocalEvent(new AnchorlessConvertedEvent(args.Target));
        _popup.PopupEntity(Loc.GetString("anchorless-devour-message"), ent.Owner, ent.Owner, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("anchorless-devoured-message"), args.Target, args.Target, PopupType.Medium);
    }
}
