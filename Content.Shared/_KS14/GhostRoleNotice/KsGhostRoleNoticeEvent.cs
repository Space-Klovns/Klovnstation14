using Robust.Shared.Serialization;

namespace Content.Shared._KS14.GhostRoleNotice;

/// <summary>
///     Server -> ghost client: a notable ghost role has appeared. The client prints the chat line in its own culture.
/// </summary>
/// <param name="RoleName">
///     The role's name, either a locale key (translated by the client when it knows it) or plain text.
/// </param>
[Serializable, NetSerializable]
public sealed class KsGhostRoleNoticeEvent(string RoleName) : EntityEventArgs
{
    public readonly string RoleName = RoleName;
}
