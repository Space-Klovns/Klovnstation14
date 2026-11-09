using Content.Server.Ghost.Roles.Components;
using Content.Server.GameTicking;
using Content.Shared._KS14.GhostRoleNotice;
using Content.Shared.Ghost;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Ghost.Roles;

// Ghost role notices: a sound and a chat line for ghosts when a notable role appears mid-round.
public sealed partial class GhostRoleSystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private EntityQuery<KsGhostRoleNotableComponent> _notableQuery = default!;
    [Dependency] private EntityQuery<GhostComponent> _ghostQuery = default!;

    private static readonly SoundSpecifier NoticeSound = new SoundPathSpecifier("/Audio/_KS14/GhostRoleNotice/ghost_role_available.ogg");

    /// <summary>
    ///     Roles that appeared this tick, announced in <see cref="UpdateNotices"/>. Checked a tick later rather than at
    ///         map init because whoever makes a role often finishes it afterwards: a command sets its name and marks it
    ///         notable, a random humanoid spawner adds the marker alongside it, a mob spawner hands it straight to a player.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingNotices = new();

    private readonly List<EntityUid> _drainedNotices = new();
    private readonly HashSet<string> _noticedRoleNames = new();

    /// <summary>
    ///     Whether this role should be announced to ghosts: marked <see cref="KsGhostRoleNotableComponent"/>, and still
    ///         up for grabs.
    /// </summary>
    public bool IsNotable(Entity<GhostRoleComponent> role)
    {
        return !role.Comp.Taken && _notableQuery.HasComp(role);
    }

    private void QueueNotice(EntityUid roleUid)
    {
        _pendingNotices.Add(roleUid);
    }

    private void UpdateNotices()
    {
        if (_pendingNotices.Count == 0)
            return;

        // Drained into a list first: telling ghosts must not be able to change the set being walked.
        _drainedNotices.Clear();
        _drainedNotices.AddRange(_pendingNotices);
        _pendingNotices.Clear();

        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        _noticedRoleNames.Clear();
        foreach (var roleUid in _drainedNotices)
        {
            if (TerminatingOrDeleted(roleUid) || !TryComp<GhostRoleComponent>(roleUid, out var ghostRoleComponent))
                continue;

            if (IsNotable((roleUid, ghostRoleComponent)))
                _noticedRoleNames.Add(ghostRoleComponent.RoleNameKey);
        }

        NotifyGhosts(_noticedRoleNames);
    }

    /// <summary>
    ///     Plays the notice sound once and sends one chat line per role name to every ghost.
    /// </summary>
    private void NotifyGhosts(IReadOnlyCollection<string> roleNames)
    {
        if (roleNames.Count == 0)
            return;

        var ghostFilter = Filter.Empty();
        var anyGhost = false;
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } attachedUid || !_ghostQuery.HasComp(attachedUid))
                continue;

            ghostFilter.AddPlayer(session);
            anyGhost = true;
        }

        if (!anyGhost)
            return;

        _audioSystem.PlayGlobal(NoticeSound, ghostFilter, recordReplay: false);
        foreach (var roleName in roleNames)
            RaiseNetworkEvent(new KsGhostRoleNoticeEvent(roleName), ghostFilter);
    }
}
