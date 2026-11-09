using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GhostRoleNotice;

/// <summary>
///     Server -> ghost client: a notable ghost role has appeared. The client prints the chat line in its own culture.
/// </summary>
/// <param name="Roles">
///     Every role of this name that appeared, for the chat line's follow link.
/// </param>
/// <param name="RoleName">
///     The role's name, either a locale key (translated by the client when it knows it) or plain text.
/// </param>
[Serializable, NetSerializable]
public sealed class KsGhostRoleNoticeEvent(string RoleName, List<NetEntity> Roles) : EntityEventArgs
{
    public readonly string RoleName = RoleName;

    public readonly List<NetEntity> Roles = Roles;
}

/// <summary>
///     Ghost client -> server: the player clicked the follow link of a notice. The server follows the first of
///         <paramref name="Roles"/> that is still free.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsGhostRoleFollowRequestEvent(List<NetEntity> Roles) : EntityEventArgs
{
    public readonly List<NetEntity> Roles = Roles;
}
