using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.GhostRespawn;
using Content.Shared._KS14.PopupLocalization;
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

/// <summary>
///     Lets a dead player's ghost respawn once a timer is up.
/// </summary>
/// <remarks>
///     Two extension points, so rules and other systems can shape respawning without this system knowing about them:
///     <list type="bullet">
///         <item><see cref="KsRefreshGhostRespawnSettingsEvent"/> replaces the cvar-driven <see cref="Settings"/>
///             (<see cref="KsGhostRespawnOverrideRuleComponent"/> does it from YAML).</item>
///         <item><see cref="KsCanGhostRespawnEvent"/> refuses a respawn whose timer is up, with a reason the ghost is
///             shown.</item>
///     </list>
/// </remarks>
public sealed partial class GhostRespawnSystem : SharedGhostRespawnSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private MindSystem _mindSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;

    /// <summary>
    ///     Penalty to be added to respawn time for each session.
    /// </summary>
    private readonly Dictionary<ICommonSession, TimeSpan> _penalties = [];
    /// <summary>
    ///     Time at which a session will respawn.
    /// </summary>
    private readonly Dictionary<ICommonSession, TimeSpan> _respawnTimes = [];
    /// <summary>
    ///     What last refused a pending session's respawn, as it was sent; no entry while nothing does.
    /// </summary>
    private readonly Dictionary<ICommonSession, GhostRespawnGate> _gates = [];
    /// <summary>
    ///     Entities that are being used to track a players respawn timer.
    ///         Think of it as their 'original body'.
    /// </summary>
    // Yea i know sessions arent removed after the entity dies but NOBODY CARES
    private readonly Dictionary<EntityUid, ICommonSession> _trackedDeathEntities = [];

    /// <summary>
    ///     Scratch list for <see cref="Update"/>.
    /// </summary>
    private readonly List<ICommonSession> _gatesToRecheck = [];

    /// <summary>
    ///     The settings as the cvars give them, before <see cref="KsRefreshGhostRespawnSettingsEvent"/>.
    /// </summary>
    private GhostRespawnSettings _cvarSettings;

    /// <summary>
    ///     What <see cref="Settings"/>' enabled state was last sent to clients as, where it differs from the cvar.
    /// </summary>
    private bool? _sentEnabledOverride;

    /// <summary>
    ///     The settings in effect: the cvars, with whatever <see cref="KsRefreshGhostRespawnSettingsEvent"/> handlers
    ///         replaced. Updated by <see cref="RefreshSettings"/>.
    /// </summary>
    public GhostRespawnSettings Settings { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.GhostRespawnEnabled, x => SetCvarSettings(_cvarSettings with { Enabled = x }), invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.GhostRespawnAlwaysStartTimer, x => SetCvarSettings(_cvarSettings with { AlwaysStartTimer = x }), invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.GhostRespawnCooldownSeconds, x => SetCvarSettings(_cvarSettings with { Cooldown = TimeSpan.FromSeconds(x) }), invokeImmediately: true);
        Subs.CVar(_configurationManager, KsCCVars.GhostRespawnPenaltySeconds, x => SetCvarSettings(_cvarSettings with { Penalty = TimeSpan.FromSeconds(x) }), invokeImmediately: true);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_gates.Count == 0)
            return;

        // A gate that said when it opens is asked again then; anything else that changes it calls RefreshGates.
        var curTime = _gameTiming.CurTime;
        _gatesToRecheck.Clear();
        foreach (var (session, gate) in _gates)
        {
            if (gate.OpensAt is { } opensAt && opensAt <= curTime)
                _gatesToRecheck.Add(session);
        }

        foreach (var session in _gatesToRecheck)
            RefreshGate(session);
    }

    [Pure]
    public bool IsSessionPendingRespawn(ICommonSession session)
        => _respawnTimes.ContainsKey(session);

    [Pure]
    public bool IsEntityTrackingThisSessionsDeath(EntityUid uid, ICommonSession session)
        => _trackedDeathEntities.TryGetValue(uid, out var trackedSession) && session == trackedSession;

    /// <summary>
    ///     The penalty the session's next death adds to its cooldown, from the respawns it has taken.
    /// </summary>
    [Pure]
    public TimeSpan GetPenalty(ICommonSession session)
        => _penalties.GetValueOrDefault(session);

    /// <summary>
    ///     When the session's respawn timer is up, if one is pending.
    /// </summary>
    [Pure]
    public TimeSpan? GetRespawnTime(ICommonSession session)
        => _respawnTimes.TryGetValue(session, out var time) ? time : null;

    /// <summary>
    ///     What refuses the session's respawn regardless of its timer, asking <see cref="KsCanGhostRespawnEvent"/>
    ///         now; null if nothing does.
    /// </summary>
    [Pure]
    public GhostRespawnGate? GetGate(ICommonSession session)
    {
        var ev = new KsCanGhostRespawnEvent(session);
        RaiseLocalEvent(ref ev);
        return ev.ToGate();
    }

    private void SetCvarSettings(GhostRespawnSettings settings)
    {
        _cvarSettings = settings;
        RefreshSettings();
    }

    /// <summary>
    ///     Works <see cref="Settings"/> out again from the cvars and <see cref="KsRefreshGhostRespawnSettingsEvent"/>.
    ///         Call it whenever a contribution to that event changes. A changed cooldown moves every pending timer by
    ///         the difference; a changed penalty applies from the next respawn.
    /// </summary>
    public void RefreshSettings()
    {
        var ev = new KsRefreshGhostRespawnSettingsEvent(_cvarSettings);
        RaiseLocalEvent(ref ev);

        var oldSettings = Settings;
        Settings = ev.Settings;

        if (Settings.Cooldown != oldSettings.Cooldown)
            ShiftPendingTimers(Settings.Cooldown - oldSettings.Cooldown);

        // Clients read the replicated cvar, so they're only told about an enabled state that differs from it.
        bool? enabledOverride = Settings.Enabled == _cvarSettings.Enabled ? null : Settings.Enabled;
        if (enabledOverride != _sentEnabledOverride)
        {
            _sentEnabledOverride = enabledOverride;
            RaiseNetworkEvent(new GhostRespawnEnabledMessage(enabledOverride));
        }
    }

    private void ShiftPendingTimers(TimeSpan delta)
    {
        foreach (var session in _respawnTimes.Keys)
        {
            ref var respawnTime = ref CollectionsMarshal.GetValueRefOrNullRef(_respawnTimes, session);
            respawnTime += delta;

            SendRespawnTime(session, respawnTime);
        }
    }

    /// <summary>
    ///     Asks <see cref="KsCanGhostRespawnEvent"/> again for every pending session and tells those whose answer
    ///         changed. Call it when something a <see cref="KsCanGhostRespawnEvent"/> handler reads changes.
    /// </summary>
    public void RefreshGates()
    {
        foreach (var session in _respawnTimes.Keys)
            RefreshGate(session);
    }

    private void RefreshGate(ICommonSession session)
    {
        if (!_respawnTimes.TryGetValue(session, out var respawnTime) || !UpdateGate(session))
            return;

        SendRespawnTime(session, respawnTime);
    }

    /// <summary>
    ///     Stores what refuses the session's respawn now. True if that changed.
    /// </summary>
    private bool UpdateGate(ICommonSession session)
    {
        var gate = GetGate(session);
        var hadGate = _gates.TryGetValue(session, out var oldGate);

        if (gate is { } newGate)
        {
            _gates[session] = newGate;
            return !hadGate || oldGate != newGate;
        }

        return _gates.Remove(session);
    }

    /// <summary>
    ///     Starts the session's respawn timer, and tells it.
    /// </summary>
    private void StartRespawnTimer(ICommonSession session, TimeSpan respawnTime)
    {
        _respawnTimes[session] = respawnTime;
        UpdateGate(session);
        SendRespawnTime(session, respawnTime);
    }

    private void ClearRespawnTimer(ICommonSession session)
    {
        _respawnTimes.Remove(session);
        _gates.Remove(session);
        SendRespawnTime(session, null);
    }

    /// <summary>
    ///     Sends the session its respawn time, and whatever gate holds it back, unless it is disconnected: there is no
    ///         channel to send on, and the stored time is resent when the session reaches
    ///         <see cref="SessionStatus.InGame"/> again.
    /// </summary>
    private void SendRespawnTime(ICommonSession session, TimeSpan? time)
    {
        if (session.Status == SessionStatus.Disconnected)
            return;

        GhostRespawnGate? gate = time != null && _gates.TryGetValue(session, out var storedGate) ? storedGate : null;
        RaiseNetworkEvent(new GhostRespawnTimeMessage(time, gate), session);
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        // Reset ghostrespawn time for everyphono
        RaiseNetworkEvent(new GhostRespawnTimeMessage(null));

        _penalties.Clear();
        _respawnTimes.Clear();
        _gates.Clear();
        _trackedDeathEntities.Clear();
    }

    [SubscribeLocalEvent]
    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (_respawnTimes.ContainsKey(args.Player) ||
            !TryComp<MobStateComponent>(args.Entity, out var mobStateComponent))
            return;

        if (!TerminatingOrDeleted(args.Entity))
            _trackedDeathEntities[args.Entity] = args.Player;

        // you can't just ghost out of it while alive (well unless the cvar allows it)
        if (!Settings.AlwaysStartTimer &&
            !IsEligibleForRespawn(args.Entity, mobStateComponent.CurrentState))
            return;

        StartRespawnTimer(args.Player, _gameTiming.CurTime + Settings.Cooldown + _penalties.GetValueOrDefault(args.Player));
    }

    [SubscribeLocalEvent]
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // If the player has died in their original body, start the respawn tracker for it.
        if (IsEligibleForRespawn(args.Target, args.NewMobState))
        {
            if (!_playerManager.TryGetSessionByEntity(args.Target, out var attachedSession) ||
                _respawnTimes.ContainsKey(attachedSession))
                return;

            StartRespawnTimer(attachedSession, _gameTiming.CurTime + Settings.Cooldown + _penalties.GetValueOrDefault(attachedSession));

            if (!TerminatingOrDeleted(args.Target))
                _trackedDeathEntities[args.Target] = attachedSession;

            return;
        }

        // Otherwise if they are now alive in their original body after dying a valid death, cancel respawn.
        if (!_trackedDeathEntities.TryGetValue(args.Target, out var session))
            return;

        ClearRespawnTimer(session);

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
        if (args.NewStatus != SessionStatus.InGame)
            return;

        if (_sentEnabledOverride != null)
            RaiseNetworkEvent(new GhostRespawnEnabledMessage(_sentEnabledOverride), args.Session);

        if (_respawnTimes.TryGetValue(args.Session, out var time))
            SendRespawnTime(args.Session, time);
    }

    [SubscribeNetworkEvent]
    private void OnActMessage(GhostRespawnActMessage message, EntitySessionEventArgs args)
    {
        if (!Settings.Enabled)
            return;

        var session = args.SenderSession;
        if (!_respawnTimes.TryGetValue(session, out var respawnTime) ||
            _gameTiming.CurTime < respawnTime)
            return;

        // Refused for now: nothing is charged, and the ghost is told why (its button may have been a tick behind).
        if (GetGate(session) is { } gate)
        {
            RefreshGate(session);

            if (session.AttachedEntity is { } attachedUid)
                _popupSystem.PopupEntity(KsPopupMessage.Create(gate.Reason), attachedUid, attachedUid, type: PopupType.MediumCaution);

            return;
        }

        if (_mindSystem.TryGetMind(session, out _, out var mindComponent) &&
            mindComponent.PreventGhosting)
        {
            // Tell them they can't ghost and thus can't respawn
            if (session.AttachedEntity is { } attachedUid)
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-ghost-respawn-popup-ghostingblocked"), attachedUid, attachedUid, type: PopupType.MediumCaution);

            return;
        }

        ref var penalty = ref CollectionsMarshal.GetValueRefOrAddDefault(_penalties, session, out _);
        penalty += Settings.Penalty;

        _respawnTimes.Remove(session);
        _gates.Remove(session);
        RaiseNetworkEvent(new GhostRespawnTimeMessage(null), session);

        _gameTicker.Respawn(session);
    }
}
