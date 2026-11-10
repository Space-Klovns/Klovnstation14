using Content.Server.Ghost.Roles.Components;
using Content.Server.GameTicking;
using Content.Shared._KS14.GhostRoleNotice;
using Content.Shared._KS14.PopupLocalization;
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
    private readonly Dictionary<string, List<EntityUid>> _noticedRoleNames = new();

    private const int MaxFollowRequestRoles = 64;

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
            {
                if (!_noticedRoleNames.TryGetValue(ghostRoleComponent.RoleNameKey, out var roles))
                {
                    roles = new List<EntityUid>();
                    _noticedRoleNames[ghostRoleComponent.RoleNameKey] = roles;
                }

                roles.Add(roleUid);
            }
        }

        NotifyGhosts(_noticedRoleNames);
    }

    /// <summary>
    ///     Plays the notice sound once and sends one chat line per role name to every ghost.
    /// </summary>
    private void NotifyGhosts(IReadOnlyDictionary<string, List<EntityUid>> roleNames)
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
        foreach (var (roleName, roleUids) in roleNames)
        {
            var roles = new List<NetEntity>(roleUids.Count);
            foreach (var roleUid in roleUids)
                roles.Add(GetNetEntity(roleUid));

            RaiseNetworkEvent(new KsGhostRoleNoticeEvent(roleName, roles), ghostFilter);
        }
    }

    /// <summary>
    ///     A ghost clicked the follow link of a notice: follow the first of its roles that is still free.
    /// </summary>
    [SubscribeNetworkEvent]
    private void OnFollowRequest(KsGhostRoleFollowRequestEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { } ghostUid || !_ghostQuery.HasComp(ghostUid))
            return;

        var checkedRoles = 0;
        foreach (var netRole in args.Roles)
        {
            if (++checkedRoles > MaxFollowRequestRoles)
                break;

            if (!TryGetEntity(netRole, out var roleUid)
                || TerminatingOrDeleted(roleUid.Value)
                || !TryComp<GhostRoleComponent>(roleUid, out var ghostRoleComponent)
                || ghostRoleComponent.Taken)
                continue;

            _followerSystem.StartFollowingEntity(ghostUid, roleUid.Value);
            return;
        }

        _popupSystem.PopupCursor(KsPopupMessage.Create("ks-ghost-role-notice-unavailable"), session.SenderSession);
    }
}
