using System.Runtime.InteropServices;
using Content.Server.GameTicking;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.GhostRespawn;

public sealed partial class GhostRespawnSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;

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
    // Yea i know sessions arent removed after the entity dies
    private readonly Dictionary<EntityUid, ICommonSession> _trackedDeathEntities = [];

    private bool _enabled;
    private TimeSpan _respawnCooldown;
    private TimeSpan _penaltyTime;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(KsCCVars.GhostRespawnEnabled, x => _enabled = x, invokeImmediately: true);
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
        _penalties.Clear();
        _respawnTimes.Clear();
        _trackedDeathEntities.Clear();
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        // you can't just ghost out of it
        if (_respawnTimes.ContainsKey(args.Player) ||
            !TryComp<MobStateComponent>(args.Entity, out var mobStateComponent))
            return;

        if (!TerminatingOrDeleted(args.Entity))
            _trackedDeathEntities[args.Entity] = args.Player;

        if (!IsEligibleForRespawn(args.Entity, mobStateComponent.CurrentState))
            return;

        var respawnTime = _gameTiming.CurTime + _respawnCooldown + _penalties.GetValueOrDefault(args.Player);
        _respawnTimes[args.Player] = respawnTime;

        RaiseNetworkEvent(new GhostRespawnTimeMessage(respawnTime), args.Player);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (!_trackedDeathEntities.TryGetValue(args.Target, out var session))
            return;

        // If the player has died in their original body, start the respawn tracker for it.
        if (IsEligibleForRespawn(args.Target, args.NewMobState))
        {
            ref var respawnTime = ref CollectionsMarshal.GetValueRefOrAddDefault(_respawnTimes, session, out var exists);
            if (exists)
                return;

            respawnTime = _gameTiming.CurTime + _respawnCooldown + _penalties.GetValueOrDefault(session);
            RaiseNetworkEvent(new GhostRespawnTimeMessage(respawnTime), session);

            if (!TerminatingOrDeleted(args.Target))
                _trackedDeathEntities[args.Target] = session;
        }
        else // Otherwise if they are now alive in their original body, cancel respawn.
        {
            _respawnTimes.Remove(session);
            RaiseNetworkEvent(new GhostRespawnTimeMessage(null), session);

            if (!TerminatingOrDeleted(args.Target))
                _trackedDeathEntities.Remove(args.Target);
        }
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

        ref var penalty = ref CollectionsMarshal.GetValueRefOrAddDefault(_penalties, args.SenderSession, out _);
        penalty += _penaltyTime;

        _respawnTimes.Remove(args.SenderSession);
        RaiseNetworkEvent(new GhostRespawnTimeMessage(null), args.SenderSession);

        _gameTicker.Respawn(args.SenderSession);
    }
}
