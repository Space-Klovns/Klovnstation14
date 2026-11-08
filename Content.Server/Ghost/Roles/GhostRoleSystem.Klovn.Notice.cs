using Content.Server.Ghost.Roles.Components;
using Content.Server.GameTicking;
using Content.Shared._KS14.GhostRoleNotice;
using Content.Shared.Ghost;
using Content.Shared.Roles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Ghost.Roles;

// Ghost role notices: a sound and a chat line for ghosts when a notable role appears mid-round.
public sealed partial class GhostRoleSystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    private static readonly SoundSpecifier NoticeSound = new SoundPathSpecifier("/Audio/_KS14/GhostRoleNotice/ghost_role_available.ogg");

    /// <summary>
    ///     Whether this role should be announced to ghosts: flagged <see cref="GhostRoleComponent.Notable"/>, or any
    ///         of its mind roles is an antagonist one.
    /// </summary>
    public bool IsNotable(Entity<GhostRoleComponent> role)
    {
        if (role.Comp.Notable)
            return true;

        foreach (var mindRoleId in role.Comp.MindRoles)
        {
            if (ProtoMan.Index(mindRoleId).TryGetComponent<MindRoleComponent>(out var mindRole, Factory) && mindRole.Antag)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Plays the notice sound and sends the chat line to every ghost. Does nothing outside a running round.
    /// </summary>
    public void NotifyGhostsOfRole(Entity<GhostRoleComponent> role)
    {
        if (_gameTicker.RunLevel != GameRunLevel.InRound)
            return;

        var ghostFilter = Filter.Empty();
        var anyGhost = false;
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } attachedUid || !HasComp<GhostComponent>(attachedUid))
                continue;

            ghostFilter.AddPlayer(session);
            anyGhost = true;
        }

        if (!anyGhost)
            return;

        _audioSystem.PlayGlobal(NoticeSound, ghostFilter, recordReplay: false);
        RaiseNetworkEvent(new KsGhostRoleNoticeEvent(role.Comp.RoleNameKey), ghostFilter);
    }
}
