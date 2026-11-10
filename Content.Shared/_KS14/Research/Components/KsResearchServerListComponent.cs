using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Research.Components;

/// <summary>
///     Lives on a map entity and lists the research servers on that map, so a client can build the
///         server selection list itself instead of waiting for the server to send it.
/// </summary>
/// <remarks>
///     Kept current by the server; never a <see cref="DataFieldAttribute"/>, so it is not saved with the map.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class KsResearchServerListComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<KsResearchServerEntry> Servers = new();
}

/// <summary>
///     One research server in a <see cref="KsResearchServerListComponent"/>.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsResearchServerEntry(NetEntity Server, int Id, string Name);
