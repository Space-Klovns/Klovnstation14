using System.Linq;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;

namespace Content.Server._KS14.Cultivation.Systems;

public sealed partial class LegendarySystem
{
    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private WumuFormationSystem _wumuFormationSystem = default!;

    /// <summary>
    ///     The secret of the saber and the sword: each manual teaches one reader a technique nobody else can learn,
    ///         usable by anyone, cultivator or not.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInheritanceManualUsed(Entity<InheritanceManualComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (entity.Comp.Used)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-manual-crumbles"));
            return;
        }

        if (_actionsSystem.GetActions(args.User).Any(action => MetaData(action).EntityPrototype?.ID == entity.Comp.Action.Id))
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-manual-known"));
            return;
        }

        entity.Comp.Used = true;
        _actionsSystem.AddAction(args.User, entity.Comp.Action);
        _visualsSystem.Play(CultivationVisualsSystem.GuqinRiseSound, args.User);
        _visualsSystem.Wave(args.User, FxColour.Gold);
        _noticeSystem.Notify(args.User, Loc.GetString(entity.Comp.Learned), PopupType.Large, alsoToChat: true);
    }

    [SubscribeLocalEvent]
    private void OnNineYinClaw(Entity<ActionsComponent> entity, ref NineYinClawActionEvent args)
    {
        if (args.Handled || args.Target == args.Performer)
            return;

        args.Handled = true;
        Shout(args.Performer, "legendary-nine-yin-shout");
        _audioSystem.PlayPvs(BladeSound, args.Target);
        _visualsSystem.Art(CultivationVisualsSystem.ArtPalmQi, args.Target);
        _visualsSystem.Distort(_transformSystem.GetMapCoordinates(args.Target), FxDistortion.Pulse);
        _damageableSystem.TryChangeDamage(args.Target, CultivationSystem.Damage("Slash", args.Damage), origin: args.Performer);
        _staminaSystem.TakeStaminaDamage(args.Target, args.StaminaDamage, source: args.Performer);
        if (TryComp<CultivatorComponent>(args.Target, out var cultivatorComponent))
            _sharedCultivationSystem.AddInternalInjury((args.Target, cultivatorComponent));

        _noticeSystem.Announce(args.Target, Loc.GetString("legendary-nine-yin-rakes", ("user", args.Performer), ("target", args.Target)),
            type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnWumuCommand(Entity<ActionsComponent> entity, ref WumuCommandActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Shout(args.Performer, "legendary-wumu-shout");
        _visualsSystem.Play(CultivationVisualsSystem.GreatBellSound, args.Performer, -6f);
        _visualsSystem.Wave(args.Performer, FxColour.Gold, big: true);

        var generalUid = args.Performer;
        var rallied = new List<EntityUid> { generalUid };
        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(generalUid).Coordinates, args.Range, _mobs);
        var from = _transformSystem.GetMapCoordinates(generalUid).Position;
        foreach (var soldier in _mobs.Where(mob => mob.Owner != generalUid && HasComp<HumanoidProfileComponent>(mob.Owner)
                         && _mobStateSystem.IsAlive(mob.Owner))
                     .OrderBy(mob => (_transformSystem.GetMapCoordinates(mob.Owner).Position - from).LengthSquared())
                     .Take(args.Soldiers))
        {
            rallied.Add(soldier.Owner);
        }

        foreach (var soldierUid in rallied)
        {
            _wumuFormationSystem.Rally(soldierUid, args.Duration);
            _staminaSystem.TakeStaminaDamage(soldierUid, -40f);
            _visualsSystem.ArtOn(CultivationVisualsSystem.ArtSheenGold, soldierUid);
            _noticeSystem.Notify(soldierUid, Loc.GetString("legendary-wumu-formation"), PopupType.Medium);
        }
    }
}
