using Content.Server._KS14.Cultivation.Components;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.Cultivation.Jianghu;
using Content.Shared._KS14.McqDialogue;
using Content.Shared.Chat;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;

namespace Content.Server._KS14.Cultivation.Systems;

// Sworn brotherhood. Light incense, offer it, and kneel together: up to three siblings for the rest of the round.
// Near each other they tire less and hit harder; they feel each other's wounds, and a sibling's death turns grief to
// fury. Turning on a sibling breaks the oath, and heaven sees it.
public sealed partial class JianghuSystem
{
    private const float SwornRange = 9f;
    private const string SwearOption = "swear";
    private const string RefuseOathOption = "refuse-oath";

    private static readonly TimeSpan SwornTogetherDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SwornVengeanceDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HurtWarningCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StrikeMemory = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan KillingBlowMemory = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan OathbreakerDuration = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan KneelDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Strikes on a sibling within <see cref="StrikeMemory"/> that break the oath.
    /// </summary>
    private const int BetrayalStrikes = 3;

    /// <summary>
    ///     A sibling whose damage passes this share of their critical threshold is badly hurt.
    /// </summary>
    private const float BadlyHurtFraction = 0.5f;

    [Dependency] private InheritanceSystem _inheritanceSystem = default!;
    [Dependency] private HeartDemonSystem _heartDemonSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifierSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;

    #region The bond

    public Entity<SwornBondComponent>? GetBond(EntityUid bodyUid)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _) || !TryComp<SwornMemberComponent>(mindId, out var memberComponent)
            || !TryComp<SwornBondComponent>(memberComponent.Bond, out var bondComponent))
            return null;

        return (memberComponent.Bond, bondComponent);
    }

    public bool AreSworn(EntityUid oneUid, EntityUid twoUid)
    {
        return GetBond(oneUid) is { } bond && _mindSystem.TryGetMind(twoUid, out var twoMind, out _) && bond.Comp.Members.Contains(twoMind);
    }

    public bool IsOathbreaker(EntityUid bodyUid)
    {
        return TryFaceComponent(bodyUid, out var faceComponent) && _gameTiming.CurTime < faceComponent.OathbrokenUntil;
    }

    /// <summary>
    ///     Why this person can't swear (with the bond they would join), as a locale id, or null if they can.
    /// </summary>
    private string? OathProblem(EntityUid bodyUid, Entity<SwornBondComponent>? joining)
    {
        if (!_mindSystem.TryGetMind(bodyUid, out _, out _))
            return "jianghu-oath-no-mind";

        var bond = GetBond(bodyUid);
        if (bond != null && bond.Value.Owner != joining?.Owner)
            return "jianghu-oath-already-sworn";

        if (bond == null && TryFaceComponent(bodyUid, out var faceComponent) && faceComponent.OathSworn)
            return "jianghu-oath-once";

        return null;
    }

    /// <summary>
    ///     Swear the two into one bond: a new one, or the one either already belongs to.
    /// </summary>
    public bool TrySwear(EntityUid oneUid, EntityUid twoUid)
    {
        var existing = GetBond(oneUid) ?? GetBond(twoUid);
        if (existing is { } existingBond && GetBond(oneUid)?.Owner == existingBond.Owner && GetBond(twoUid)?.Owner == existingBond.Owner)
            return false;

        if (existing is { } fullBond && fullBond.Comp.Members.Count >= SwornBondComponent.MaxMembers
            || OathProblem(oneUid, existing) != null || OathProblem(twoUid, existing) != null)
            return false;

        Entity<SwornBondComponent> bond;
        if (existing is { } joined)
        {
            bond = joined;
        }
        else
        {
            var bondUid = Spawn(null, MapCoordinates.Nullspace);
            bond = (bondUid, AddComp<SwornBondComponent>(bondUid));
        }

        foreach (var bodyUid in new[] { oneUid, twoUid })
        {
            if (!_mindSystem.TryGetMind(bodyUid, out var mindId, out _) || !bond.Comp.Members.Add(mindId))
                continue;

            EnsureComp<SwornMemberComponent>(mindId).Bond = bond;
            if (EnsureFaceComponent(bodyUid) is { } faceComponent)
                faceComponent.OathSworn = true;

            // A sworn sibling can be named heir.
            _inheritanceSystem.GrantNameHeir(bodyUid);

            _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(bodyUid):player} swore brotherhood");
        }

        return true;
    }

    private IEnumerable<EntityUid> SiblingBodies(SwornBondComponent bondComponent)
    {
        foreach (var mindId in bondComponent.Members)
        {
            if (TryComp<MindComponent>(mindId, out var mindComponent) && mindComponent.OwnedEntity is { } bodyUid
                && !TerminatingOrDeleted(bodyUid) && _mobStateSystem.IsAlive(bodyUid))
                yield return bodyUid;
        }
    }

    private void UpdateBonds(TimeSpan now)
    {
        var bondQuery = EntityQueryEnumerator<SwornBondComponent>();
        while (bondQuery.MoveNext(out _, out var bondComponent))
        {
            var bodies = new List<EntityUid>(SiblingBodies(bondComponent));
            foreach (var bodyUid in bodies)
            {
                foreach (var siblingUid in bodies)
                {
                    if (siblingUid == bodyUid || !_transformSystem.InRange((bodyUid, null), (siblingUid, null), SwornRange))
                        continue;

                    EnsureComp<SwornTogetherComponent>(bodyUid).ExpiresAt = now + SwornTogetherDuration;
                    break;
                }

                WarnIfBadlyHurt(bondComponent, bodyUid, bodies, now);
            }
        }

        var togetherQuery = EntityQueryEnumerator<SwornTogetherComponent>();
        while (togetherQuery.MoveNext(out var bodyUid, out var togetherComponent))
        {
            if (now >= togetherComponent.ExpiresAt)
                RemCompDeferred<SwornTogetherComponent>(bodyUid);
        }

        var vengeanceQuery = EntityQueryEnumerator<SwornVengeanceComponent>();
        while (vengeanceQuery.MoveNext(out var bodyUid, out var vengeanceComponent))
        {
            if (now < vengeanceComponent.ExpiresAt || vengeanceComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            RemCompDeferred<SwornVengeanceComponent>(bodyUid);
            _noticeSystem.Notify(bodyUid, Loc.GetString("jianghu-vengeance-cools"));
        }
    }

    /// <summary>
    ///     When one is badly hurt, the others feel it and know which way to run.
    /// </summary>
    private void WarnIfBadlyHurt(SwornBondComponent bondComponent, EntityUid hurtUid, List<EntityUid> bodies, TimeSpan now)
    {
        if (!_mindSystem.TryGetMind(hurtUid, out var hurtMind, out _)
            || bondComponent.HurtWarnings.TryGetValue(hurtMind, out var nextWarning) && now < nextWarning
            || !_mobThresholdSystem.TryGetThresholdForState(hurtUid, MobState.Critical, out var critThreshold)
            || _damageableSystem.GetTotalDamage(hurtUid) < critThreshold.Value * BadlyHurtFraction)
            return;

        bondComponent.HurtWarnings[hurtMind] = now + HurtWarningCooldown;
        var hurtPosition = _transformSystem.GetMapCoordinates(hurtUid);
        foreach (var siblingUid in bodies)
        {
            if (siblingUid == hurtUid)
                continue;

            var siblingPosition = _transformSystem.GetMapCoordinates(siblingUid);
            var where = siblingPosition.MapId == hurtPosition.MapId
                ? Loc.GetString("jianghu-sworn-direction", ("direction",
                    Loc.GetString($"jianghu-direction-{(hurtPosition.Position - siblingPosition.Position).ToWorldAngle().GetDir()}")))
                : Loc.GetString("jianghu-sworn-far-away");
            _noticeSystem.Notify(siblingUid, Loc.GetString("jianghu-sworn-hurt", ("name", Name(hurtUid)), ("where", where)), type: PopupType.LargeCaution);
        }
    }

    /// <summary>
    ///     Striking a sibling is a warning; strike again and again and the oath is broken. Sparring in an honor duel
    ///         between the two of them doesn't count.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnJianghuDamaged(Entity<JianghuExamineComponent> entity, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } strikerUid || strikerUid == entity.Owner || !AreSworn(entity, strikerUid)
            || TryComp<HonorDuelComponent>(entity, out var duelComponent) && duelComponent.Opponent == strikerUid
            || GetBond(entity) is not { } bond || !_mindSystem.TryGetMind(strikerUid, out var strikerMind, out _))
            return;

        var now = _gameTiming.CurTime;
        var strikes = bond.Comp.Strikes.GetValueOrDefault(strikerMind) ?? new List<TimeSpan>();
        strikes.RemoveAll(strikeTime => strikeTime < now - StrikeMemory);
        strikes.Add(now);
        bond.Comp.Strikes[strikerMind] = strikes;

        if (strikes.Count >= BetrayalStrikes)
            Betray(bond, strikerUid, entity);
        else if (strikes.Count == 1)
            _noticeSystem.Notify(strikerUid, Loc.GetString("jianghu-sworn-struck-sibling"), type: PopupType.MediumCaution);
    }

    /// <summary>
    ///     A sibling's death: if another sibling just struck them, that was betrayal; otherwise the rest are furious.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnJianghuMobState(Entity<JianghuExamineComponent> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || GetBond(entity) is not { } bond)
            return;

        var now = _gameTiming.CurTime;
        var siblings = new List<EntityUid>(SiblingBodies(bond.Comp));
        foreach (var siblingUid in siblings)
        {
            if (_mindSystem.TryGetMind(siblingUid, out var siblingMind, out _)
                && bond.Comp.Strikes.TryGetValue(siblingMind, out var strikes) && strikes.Count > 0
                && strikes[^1] > now - KillingBlowMemory)
            {
                Betray(bond, siblingUid, entity);
                return;
            }
        }

        foreach (var siblingUid in siblings)
        {
            EnsureComp<SwornVengeanceComponent>(siblingUid).ExpiresAt = now + SwornVengeanceDuration;
            _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(siblingUid);
            _noticeSystem.Notify(siblingUid, Loc.GetString("jianghu-sworn-died", ("name", Name(entity))), type: PopupType.LargeCaution);
        }
    }

    /// <summary>
    ///     The oath is broken: the bond dissolves, the betrayer is shamed, and a cultivator's guilt tears loose.
    /// </summary>
    public void Betray(Entity<SwornBondComponent> bond, EntityUid betrayerUid, EntityUid betrayedUid)
    {
        _noticeSystem.Announce(betrayerUid, Loc.GetString("jianghu-sworn-betrayal", ("user", betrayerUid), ("target", betrayedUid)), type: PopupType.LargeCaution);
        foreach (var siblingUid in SiblingBodies(bond.Comp))
        {
            if (siblingUid != betrayerUid)
                _noticeSystem.Notify(siblingUid, Loc.GetString("jianghu-sworn-broken", ("name", Name(betrayerUid))), type: PopupType.LargeCaution);
        }

        if (EnsureFaceComponent(betrayerUid) is { } faceComponent)
            faceComponent.OathbrokenUntil = _gameTiming.CurTime + OathbreakerDuration;

        AdjustFace(betrayerUid, -10, "jianghu-reason-betrayal");
        if (_cultivatorQuery.HasComp(betrayerUid))
            _heartDemonSystem.Summon(betrayerUid);

        foreach (var mindId in bond.Comp.Members)
            RemComp<SwornMemberComponent>(mindId);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(betrayerUid):player} broke their oath of brotherhood by attacking {ToPrettyString(betrayedUid):target}");
        QueueDel(bond);
    }

    #endregion

    #region Together and vengeance

    [SubscribeLocalEvent]
    private void OnTogetherStamina(Entity<SwornTogetherComponent> entity, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value > 0)
            args.Value *= entity.Comp.StaminaMultiplier;
    }

    [SubscribeLocalEvent]
    private void OnTogetherMeleeHit(Entity<SwornTogetherComponent> entity, ref MeleeHitEvent args)
    {
        if (args.IsHit && args.HitEntities.Count > 0)
            args.BonusDamage += CultivationSystem.Damage("Blunt", entity.Comp.BonusDamage);
    }

    [SubscribeLocalEvent]
    private void OnVengeanceStamina(Entity<SwornVengeanceComponent> entity, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value > 0)
            args.Value *= entity.Comp.StaminaMultiplier;
    }

    [SubscribeLocalEvent]
    private void OnVengeanceMeleeHit(Entity<SwornVengeanceComponent> entity, ref MeleeHitEvent args)
    {
        if (args.IsHit && args.HitEntities.Count > 0)
            args.BonusDamage += CultivationSystem.Damage("Blunt", entity.Comp.BonusDamage);
    }

    [SubscribeLocalEvent]
    private void OnVengeanceSpeed(Entity<SwornVengeanceComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (entity.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ModifySpeed(entity.Comp.SpeedMultiplier, entity.Comp.SpeedMultiplier);
    }

    [SubscribeLocalEvent]
    private void OnVengeanceRemoved(Entity<SwornVengeanceComponent> entity, ref ComponentRemove args)
    {
        _movementSpeedModifierSystem.RefreshMovementSpeedModifiers(entity);
    }

    #endregion

    #region Incense

    [SubscribeLocalEvent]
    private void OnIncenseExamined(Entity<SwornIncenseComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(entity.Comp.Lit ? "jianghu-incense-lit" : "jianghu-incense-unlit"));
    }

    [SubscribeLocalEvent]
    private void OnIncenseUsed(Entity<SwornIncenseComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled || entity.Comp.Lit)
            return;

        args.Handled = true;
        entity.Comp.Lit = true;
        entity.Comp.BurnsOutAt = _gameTiming.CurTime + entity.Comp.BurnTime;
        _appearanceSystem.SetData(entity, SwornIncenseVisuals.Lit, true);
        _noticeSystem.Announce(args.User, Loc.GetString("jianghu-incense-lights", ("user", args.User)));
    }

    [SubscribeLocalEvent]
    private void OnIncenseOffered(Entity<SwornIncenseComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } otherUid || otherUid == args.User
            || !_mindSystem.TryGetMind(otherUid, out _, out _))
            return;

        args.Handled = true;
        if (!entity.Comp.Lit)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("jianghu-incense-light-first"));
            return;
        }

        var existing = GetBond(args.User) ?? GetBond(otherUid);
        if (existing is { } fullBond && fullBond.Comp.Members.Count >= SwornBondComponent.MaxMembers)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("jianghu-oath-full"));
            return;
        }

        foreach (var swearerUid in new[] { args.User, otherUid })
        {
            if (OathProblem(swearerUid, existing) is { } problem)
            {
                _noticeSystem.Notify(args.User, Loc.GetString(problem, ("target", swearerUid)));
                return;
            }
        }

        entity.Comp.Offerer = args.User;
        entity.Comp.Offeree = otherUid;
        _noticeSystem.Announce(args.User, Loc.GetString("jianghu-oath-offered", ("user", args.User), ("target", otherUid)), type: PopupType.Medium);
        _mcqDialogueSystem.StartDialogue(entity, otherUid, new McqDialogueData[]
        {
            new("jianghu-oath-option-swear", SwearOption),
            new("jianghu-oath-option-refuse", RefuseOathOption),
        }, new McqDialogueSettings { Range = 4f });
    }

    [SubscribeLocalEvent]
    private void OnOathAnswered(Entity<SwornIncenseComponent> entity, ref McqDialogueSelectedEvent args)
    {
        if (args.Actor != entity.Comp.Offeree || entity.Comp.Offerer is not { } offererUid || TerminatingOrDeleted(offererUid))
            return;

        if (args.Id != SwearOption)
        {
            _noticeSystem.Notify(offererUid, Loc.GetString("jianghu-oath-declined", ("target", args.Actor)));
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, offererUid, KneelDuration, new SwornOathDoAfterEvent(), entity, target: args.Actor, used: entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
        };

        if (_doAfterSystem.TryStartDoAfter(doAfterArgs))
            _noticeSystem.Announce(offererUid, Loc.GetString("jianghu-oath-kneel", ("user", offererUid), ("target", args.Actor)), type: PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnOathSworn(Entity<SwornIncenseComponent> entity, ref SwornOathDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } otherUid)
            return;

        args.Handled = true;
        if (!TrySwear(args.User, otherUid))
            return;

        _chatSystem.TrySendInGameICMessage(args.User, Loc.GetString("jianghu-oath-words-one"), InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
        _chatSystem.TrySendInGameICMessage(otherUid, Loc.GetString("jianghu-oath-words-two"), InGameICChatType.Speak, hideChat: false, ignoreActionBlocker: true);
        _visualsSystem.Wave(args.User, FxColour.Gold, big: true);
        _visualsSystem.Motes(args.User, FxColour.Gold, TimeSpan.FromSeconds(3));
        _visualsSystem.Motes(otherUid, FxColour.Gold, TimeSpan.FromSeconds(3));
        foreach (var swornUid in new[] { args.User, otherUid })
            _noticeSystem.Notify(swornUid, Loc.GetString("jianghu-oath-sworn"), type: PopupType.Large);

        QueueDel(entity);
    }

    private void UpdateIncense(TimeSpan now)
    {
        var incenseQuery = EntityQueryEnumerator<SwornIncenseComponent>();
        while (incenseQuery.MoveNext(out var incenseUid, out var incenseComponent))
        {
            if (!incenseComponent.Lit || now < incenseComponent.BurnsOutAt || EntityManager.IsQueuedForDeletion(incenseUid))
                continue;

            _noticeSystem.Announce(incenseUid, Loc.GetString("jianghu-incense-burns-down"));
            QueueDel(incenseUid);
        }
    }

    #endregion
}
