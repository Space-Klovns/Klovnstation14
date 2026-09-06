using Content.Server.Antag;
using Content.Server._KS14.GameTicking.Rules.Components;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._KS14.Anchorless.Systems;

/// <summary>Server-authoritative recruitment. Role assignment and mind transfer cannot be predicted.</summary>
public sealed partial class AnchorlessConversionSystem : EntitySystem
{
    [Dependency] private AntagSelectionSystem _antags = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KsAnchorlessAntagComponent, AnchorlessConvertActionEvent>(OnConvert);
        SubscribeLocalEvent<KsAnchorlessAntagComponent, AnchorlessConvertDoAfterEvent>(OnConvertDoAfter);
    }

    private void OnConvert(Entity<KsAnchorlessAntagComponent> ent, ref AnchorlessConvertActionEvent args)
    {
        if (args.Handled || !CanConvert(args.Target))
            return;

        args.Handled = true;
        _audio.PlayPvs(new SoundCollectionSpecifier("ChangelingDevourWindup", AudioParams.Default.WithMaxDistance(6)), ent);
        _popup.PopupEntity(Loc.GetString("anchorless-convert-begin-message"), ent.Owner, PopupType.LargeCaution);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent.Owner, ent.Comp.ConversionTimespan,
            new AnchorlessConvertDoAfterEvent(), ent.Owner, target: args.Target, used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.None,
        });
    }

    private void OnConvertDoAfter(Entity<KsAnchorlessAntagComponent> ent, ref AnchorlessConvertDoAfterEvent args)
    {
        args.Handled = true;
        if (args.Cancelled || args.Target is not { } target || !CanConvert(target) ||
            !_mind.TryGetMind(target, out _, out var mind) ||
            !_players.TryGetSessionById(mind.UserId, out var session))
            return;

        _damage.ClearAllDamage(target);
        _mobState.ChangeMobState(target, MobState.Alive);
        _antags.ForceMakeAntag<AnchorlessRuleComponent>(session, "Anchorless");
        var convertedEvent = new AnchorlessConvertedEvent(target);
        RaiseLocalEvent(ref convertedEvent);

        _audio.PlayPvs(new SoundCollectionSpecifier("ChangelingDevourConsume", AudioParams.Default.WithMaxDistance(6)), ent);
        _popup.PopupEntity(Loc.GetString("anchorless-devour-message"), ent.Owner, ent.Owner, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("anchorless-devoured-message"), target, target, PopupType.Medium);
    }

    private bool CanConvert(EntityUid target)
        => !HasComp<KsAnchorlessAntagComponent>(target) &&
           (_mobState.IsCritical(target) || _mobState.IsDead(target)) &&
           _mind.TryGetMind(target, out _, out var mind) &&
           _players.TryGetSessionById(mind.UserId, out _);
}

/// <summary>Raised after a crew member has been successfully remade as Anchorless.</summary>
[ByRefEvent]
public record struct AnchorlessConvertedEvent(EntityUid Converted);
