using System.Numerics;
using System.Text;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     The wuxia fighting styles learned from manuals. Every punch, shove and grab is remembered, and the right streak
///         against the same opponent lands one of the style's combos.
/// </summary>
public sealed partial class WuxiaStyleSystem : EntitySystem
{
    private const char Punch = 'H';
    private const char Shove = 'D';
    private const char Grab = 'G';

    /// <summary>
    ///     How long a streak waits for its next move.
    /// </summary>
    private static readonly TimeSpan StreakTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan StickyHandsDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Each style's combos, by the streak that lands them, checked longest first.
    /// </summary>
    private static readonly Dictionary<WuxiaStyle, (string Streak, string Combo)[]> Combos = new()
    {
        [WuxiaStyle.DrunkenFist] = new[] { ("HDD", "staggering-lotus"), ("DH", "immortal-wine"), ("GH", "drunken-embrace") },
        [WuxiaStyle.EagleClaw] = new[] { ("HHH", "claw-rake"), ("GH", "eagle-seizes"), ("DG", "joint-lock") },
        [WuxiaStyle.WingChun] = new[] { ("HHHH", "chain-punch"), ("DD", "sticky-hands"), ("HD", "centerline") },
    };

    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedCombatModeSystem _combatModeSystem = default!;
    [Dependency] private SharedCultivationSystem _sharedCultivationSystem = default!;
    [Dependency] private SharedDrunkSystem _drunkSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private CultivationVisualsSystem _visualsSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;
    [Dependency] private SharedStaminaSystem _staminaSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private CultivationStanceSystem _stanceSystem = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;
    [Dependency] private EntityQuery<WuxiaStyleComponent> _styleQuery = default!;

    #region Reading the streak

    [SubscribeLocalEvent]
    private void OnStyleMeleeHit(Entity<WuxiaStyleComponent> entity, ref MeleeHitEvent args)
    {
        // Only bare hands: the event is raised on the user only when they are their own weapon.
        if (!args.IsHit || args.HitEntities.Count == 0 || args.Weapon != entity.Owner)
            return;

        var defenderUid = args.HitEntities[0];
        if (!HasComp<MobStateComponent>(defenderUid))
            return;

        args.BonusDamage += PunchBonus(entity);
        if (entity.Comp.Style == WuxiaStyle.WingChun)
            _staminaSystem.TakeStaminaDamage(defenderUid, 5f, source: entity);

        AddToStreak(entity, defenderUid, Punch);
    }

    /// <summary>
    ///     A shove is only reported to the one shoved, so the style listens on every mob for shoves from a stylist.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMobShoved(Entity<MobStateComponent> entity, ref DisarmedEvent args)
    {
        if (_styleQuery.TryComp(args.Source, out var styleComponent))
            AddToStreak((args.Source, styleComponent), entity, Shove);
    }

    [SubscribeLocalEvent]
    private void OnStyleGrab(Entity<WuxiaStyleComponent> entity, ref PullStartedMessage args)
    {
        if (args.PullerUid == entity.Owner && HasComp<MobStateComponent>(args.PulledUid))
            AddToStreak(entity, args.PulledUid, Grab);
    }

    /// <summary>
    ///     Remember a move, and land a combo if the streak now ends in one.
    /// </summary>
    public void AddToStreak(Entity<WuxiaStyleComponent> entity, EntityUid defenderUid, char move)
    {
        var now = _gameTiming.CurTime;
        if (entity.Comp.StreakTarget != defenderUid || now - entity.Comp.LastInput > StreakTimeout)
            entity.Comp.Streak = string.Empty;

        entity.Comp.StreakTarget = defenderUid;
        entity.Comp.LastInput = now;
        entity.Comp.Streak += move;

        foreach (var (streak, combo) in Combos[entity.Comp.Style])
        {
            if (!entity.Comp.Streak.EndsWith(streak, StringComparison.Ordinal))
                continue;

            entity.Comp.Streak = string.Empty;
            PerformCombo(entity, defenderUid, combo);

            // Pulling off a combo is good practice.
            _sharedCultivationSystem.AddInsight(entity.Owner, 2f);
            return;
        }

        // Nothing longer than the longest combo is worth keeping.
        if (entity.Comp.Streak.Length > 4)
            entity.Comp.Streak = entity.Comp.Streak[^4..];
    }

    #endregion

    #region The styles

    private DamageSpecifier PunchBonus(Entity<WuxiaStyleComponent> entity)
    {
        return entity.Comp.Style switch
        {
            WuxiaStyle.DrunkenFist => CultivationSystem.Damage("Blunt", 3f * (float)Drunkenness(entity)),
            WuxiaStyle.EagleClaw => CultivationSystem.Damage("Slash", 2f),
            _ => new DamageSpecifier(),
        };
    }

    /// <summary>
    ///     0 sober, up to 3 properly sloshed.
    /// </summary>
    public int Drunkenness(EntityUid uid)
    {
        if (!TryComp<StatusEffectContainerComponent>(uid, out var containerComponent)
            || !_statusEffectsSystem.TryGetTime(uid, SharedDrunkSystem.Drunk, out var drunkTime, containerComponent)
            || drunkTime.EndEffectTime is not { } endTime)
            return 0;

        var remainingSeconds = (endTime - _gameTiming.CurTime).TotalSeconds;
        return remainingSeconds switch
        {
            >= 60 => 3,
            >= 30 => 2,
            >= 10 => 1,
            _ => 0,
        };
    }

    private void PerformCombo(Entity<WuxiaStyleComponent> entity, EntityUid defenderUid, string combo)
    {
        var attackerUid = entity.Owner;
        var drunkenness = Drunkenness(attackerUid);
        switch (combo)
        {
            case "staggering-lotus":
                Strike(attackerUid, defenderUid, "Blunt", 6f + 3f * (float)drunkenness);
                _stunSystem.TryKnockdown(defenderUid, TimeSpan.FromSeconds(2), refresh: true);
                _stanceSystem.ApplySnare(defenderUid, TimeSpan.FromSeconds(10), 0.7f);
                break;
            case "immortal-wine":
                Strike(attackerUid, defenderUid, "Blunt", 14f + 5f * (float)drunkenness);
                Fling(attackerUid, defenderUid, 1f + (float)drunkenness);
                break;
            case "drunken-embrace":
                Strike(attackerUid, defenderUid, "Blunt", 8f);
                _drunkSystem.TryApplyDrunkenness(defenderUid, TimeSpan.FromSeconds(6));
                _drunkSystem.TryApplyDrunkenness(attackerUid, TimeSpan.FromSeconds(2));
                break;
            case "claw-rake":
                Strike(attackerUid, defenderUid, "Slash", 18f);
                _bloodstreamSystem.TryModifyBleedAmount(defenderUid, 3f);
                break;
            case "eagle-seizes":
                _staminaSystem.TakeStaminaDamage(defenderUid, 35f, source: attackerUid);
                _handsSystem.DropAll(defenderUid, checkActionBlocker: false);
                break;
            case "joint-lock":
                Strike(attackerUid, defenderUid, "Blunt", 15f);
                _staminaSystem.TakeStaminaDamage(defenderUid, 15f, source: attackerUid);
                if (_handsSystem.GetActiveItem(defenderUid) is { } heldUid)
                    _handsSystem.TryDrop(defenderUid, heldUid, checkActionBlocker: false);
                break;
            case "chain-punch":
                for (var blow = 0; blow < 4; blow++)
                {
                    Strike(attackerUid, defenderUid, "Blunt", 5f);
                    _staminaSystem.TakeStaminaDamage(defenderUid, 8f, source: attackerUid);
                }
                break;
            case "sticky-hands":
                entity.Comp.StickyUntil = _gameTiming.CurTime + StickyHandsDuration;
                break;
            case "centerline":
                Strike(attackerUid, defenderUid, "Blunt", 10f);
                _staminaSystem.TakeStaminaDamage(defenderUid, 30f, source: attackerUid);
                if (TryComp<Content.Shared.Damage.Components.StaminaComponent>(defenderUid, out var staminaComponent)
                    && staminaComponent.StaminaDamage >= 50f)
                    _stunSystem.TryKnockdown(defenderUid, TimeSpan.FromSeconds(1.5), refresh: true);
                break;
        }

        _visualsSystem.Strike(attackerUid, defenderUid, FxColour.Qi);
        _visualsSystem.Cast(attackerUid, FxColour.Qi);
        _noticeSystem.Announce(defenderUid, Loc.GetString($"wuxia-combo-{combo}", ("user", attackerUid), ("target", defenderUid)), type: PopupType.MediumCaution);
        if (Loc.TryGetString($"wuxia-shout-{combo}", out var shout))
            _chatSystem.TrySendInGameICMessage(attackerUid, shout, InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
    }

    private void Strike(EntityUid attackerUid, EntityUid defenderUid, string damageType, float amount)
    {
        _damageableSystem.TryChangeDamage(defenderUid, CultivationSystem.Damage(damageType, amount), origin: attackerUid);
    }

    private void Fling(EntityUid attackerUid, EntityUid defenderUid, float distance)
    {
        var direction = _transformSystem.GetWorldPosition(defenderUid) - _transformSystem.GetWorldPosition(attackerUid);
        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector2(0f, 1f);

        _throwingSystem.TryThrow(defenderUid, Vector2.Normalize(direction) * distance, baseThrowSpeed: 8f, user: attackerUid, recoil: false);
    }

    /// <summary>
    ///     The Drunken Fist sways out of the way of blows the drunker it is; Wing Chun's Sticky Hands turn aside one.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStyleDamageModify(Entity<WuxiaStyleComponent> entity, ref DamageModifyEvent args)
    {
        if (args.Origin is not { } attackerUid || attackerUid == entity.Owner
            || !_transformSystem.InRange((attackerUid, null), (entity.Owner, null), 2f))
            return;

        var deflected = false;
        if (entity.Comp.Style == WuxiaStyle.WingChun && _gameTiming.CurTime < entity.Comp.StickyUntil)
        {
            entity.Comp.StickyUntil = TimeSpan.Zero;
            deflected = true;
        }
        else if (entity.Comp.Style == WuxiaStyle.DrunkenFist && _combatModeSystem.IsInCombatMode(entity)
            && Drunkenness(entity) is var drunkenness and > 0 && _robustRandom.Prob(0.1f + 0.1f * (float)drunkenness))
        {
            deflected = true;
        }

        if (!deflected)
            return;

        args.Damage *= 0f;
        _noticeSystem.Announce(entity, Loc.GetString(entity.Comp.Style == WuxiaStyle.WingChun ? "wuxia-sticky-deflect" : "wuxia-drunken-sway",
            ("user", entity.Owner)), type: PopupType.Medium);
    }

    #endregion

    [SubscribeLocalEvent]
    private void OnRecallTeachings(Entity<WuxiaStyleComponent> entity, ref RecallTeachingsActionEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(entity, out var actorComponent))
            return;

        args.Handled = true;
        var style = entity.Comp.Style;
        var message = new StringBuilder();
        message.AppendLine(Loc.GetString($"wuxia-teachings-{style}"));
        foreach (var (_, combo) in Combos[style])
            message.AppendLine(Loc.GetString($"wuxia-teaching-{combo}"));

        var text = message.ToString().TrimEnd();
        _chatManager.ChatMessageToOne(ChatChannel.Server, text, text, EntityUid.Invalid, hideChat: false,
            actorComponent.PlayerSession.Channel, colorOverride: Color.FromHex("#c9a227"));
    }
}
