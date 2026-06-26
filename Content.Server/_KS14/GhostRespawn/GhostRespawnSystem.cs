using System.Runtime.InteropServices;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.GhostRespawn;

/*
    The reason why the ghost's mind's MindComponent.TimeOfDeath isn't used is because
        i don't trust it and i think it's a liability.

    Also because the respawn timer is only relative to your *first* 'death' (after your last respawn, if any);
        so if you take a ghostrole and die/suicide/whatever in it, your respawn timer will be unaffected.
*/

public sealed partial class GhostRespawnSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    /// <summary>
    ///     Penalty to be added to respawn time for each session.
    /// </summary>
    private readonly Dictionary<ICommonSession, TimeSpan> _penalties = [];
    /// <summary>
    ///     Time at which a session will respawn.
    /// </summary>
    private readonly Dictionary<ICommonSession, TimeSpan> _respawnTimes = [];
    /// <summary>
    ///     Entities that are being used to track a players respawn timer.
    ///         Think of it as their 'original body'.
    /// </summary>
    // Yea i know sessions arent removed after the entity dies but NOBODY CARES
    private readonly Dictionary<EntityUid, ICommonSession> _trackedDeathEntities = [];

    private bool _enabled;
    private bool _alwaysStartTimer;
    private TimeSpan _respawnCooldown;
    private TimeSpan _penaltyTime;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.GhostRespawnEnabled, x => _enabled = x, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.GhostRespawnAlwaysStartTimer, x => _alwaysStartTimer = x, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.GhostRespawnCooldownSeconds, OnCooldownChanged, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.GhostRespawnPenaltySeconds, x => _penaltyTime = TimeSpan.FromSeconds(x), invokeImmediately: true);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeNetworkEvent<GhostRespawnActMessage>(OnActMessage);
    }

    private void OnCooldownChanged(float newValue)
    {
        var newTime = TimeSpan.FromSeconds(newValue);
        var delta = newTime - _respawnCooldown;

        foreach (var session in _respawnTimes.Keys)
        {
            ref var respawnTime = ref CollectionsMarshal.GetValueRefOrNullRef(_respawnTimes, session);
            respawnTime += delta;

            RaiseNetworkEvent(new GhostRespawnTimeMessage(respawnTime), session);
        }

        _respawnCooldown = newTime;
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        // Reset ghostrespawn time for everyphono
        RaiseNetworkEvent(new GhostRespawnTimeMessage(null));

        _penalties.Clear();
        _respawnTimes.Clear();
        _trackedDeathEntities.Clear();
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (_respawnTimes.ContainsKey(args.Player) ||
            !TryComp<MobStateComponent>(args.Entity, out var mobStateComponent))
            return;

        if (!TerminatingOrDeleted(args.Entity))
            _trackedDeathEntities[args.Entity] = args.Player;

        // you can't just ghost out of it while alive (well unless the cvar allows it)
        if (!_alwaysStartTimer &&
            !IsEligibleForRespawn(args.Entity, mobStateComponent.CurrentState))
            return;

        var respawnTime = _gameTiming.CurTime + _respawnCooldown + _penalties.GetValueOrDefault(args.Player);
        _respawnTimes[args.Player] = respawnTime;

        RaiseNetworkEvent(new GhostRespawnTimeMessage(respawnTime), args.Player);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // If the player has died in their original body, start the respawn tracker for it.
        if (IsEligibleForRespawn(args.Target, args.NewMobState))
        {
            if (!_playerManager.TryGetSessionByEntity(args.Target, out var attachedSession))
                return;

            ref var respawnTime = ref CollectionsMarshal.GetValueRefOrAddDefault(_respawnTimes, attachedSession, out var exists);
            if (exists)
                return;

            respawnTime = _gameTiming.CurTime + _respawnCooldown + _penalties.GetValueOrDefault(attachedSession);
            RaiseNetworkEvent(new GhostRespawnTimeMessage(respawnTime), attachedSession);

            if (!TerminatingOrDeleted(args.Target))
                _trackedDeathEntities[args.Target] = attachedSession;

            return;
        }

        // Otherwise if they are now alive in their original body after dying a valid death, cancel respawn.
        if (!_trackedDeathEntities.TryGetValue(args.Target, out var session))
            return;

        _respawnTimes.Remove(session);
        RaiseNetworkEvent(new GhostRespawnTimeMessage(null), session);

        if (!TerminatingOrDeleted(args.Target))
            _trackedDeathEntities.Remove(args.Target);
    }

    private bool IsEligibleForRespawn(EntityUid uid, MobState state)
    {
        if (TerminatingOrDeleted(uid))
            return true;

        return state == MobState.Dead;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame ||
            !_respawnTimes.TryGetValue(args.Session, out var time))
            return;

        RaiseNetworkEvent(new GhostRespawnTimeMessage(time), args.Session);
    }

    private void OnActMessage(GhostRespawnActMessage message, EntitySessionEventArgs args)
    {
        if (!_enabled)
            return;

        if (!_respawnTimes.TryGetValue(args.SenderSession, out var respawnTime) ||
            _gameTiming.CurTime < respawnTime)
            return;

        if (_mindSystem.TryGetMind(args.SenderSession, out _, out var mindComponent) &&
            mindComponent.PreventGhosting)
        {
            // Tell them they can't ghost and thus can't respawn
            if (args.SenderSession.AttachedEntity is { } attachedUid)
                _popupSystem.PopupEntity("ks-ghost-respawn-popup-ghostingblocked", attachedUid, attachedUid, type: PopupType.MediumCaution);

            return;
        }

        ref var penalty = ref CollectionsMarshal.GetValueRefOrAddDefault(_penalties, args.SenderSession, out _);
        penalty += _penaltyTime;

        _respawnTimes.Remove(args.SenderSession);
        RaiseNetworkEvent(new GhostRespawnTimeMessage(null), args.SenderSession);

        _gameTicker.Respawn(args.SenderSession);
    }
}
