using Robust.Shared.GameStates; // KS14
using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components
{
    /// <summary>
    /// This is an entity that is able to connect to a <see cref="ResearchServerComponent"/>
    /// </summary>
    [RegisterComponent, NetworkedComponent /* KS14: added */, AutoGenerateComponentState(raiseAfterAutoHandleState: true) /* KS14: added */]
    public sealed partial class ResearchClientComponent : Component
    {
        public bool ConnectedToServer => Server != null;

        /// <summary>
        /// The server the client is connected to
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly)]
        public EntityUid? Server { get; set; }

        // KS14 start: the server entity may be outside the client's PVS, so its id is networked instead
        /// <summary>
        /// The id of <see cref="Server"/>, or -1. Networked so a client can show which server is selected.
        /// </summary>
        [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
        public int ConnectedServerId = -1;
        // KS14 end
    }

    /// <summary>
    /// Raised on the client whenever its server is changed
    /// </summary>
    /// <param name="Server">Its new server</param>
    [ByRefEvent]
    public readonly record struct ResearchRegistrationChangedEvent(EntityUid? Server);

    /// <summary>
    ///     Sent to the server when the client deselects a research server.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ResearchClientServerDeselectedMessage : BoundUserInterfaceMessage
    {
    }

    /// <summary>
    ///     Sent to the server when the client chooses a research server.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ResearchClientServerSelectedMessage : BoundUserInterfaceMessage
    {
        public int ServerId;

        public ResearchClientServerSelectedMessage(int serverId)
        {
            ServerId = serverId;
        }
    }

    // KS14: removed ResearchClientSyncMessage, the server list is networked component state

    [NetSerializable, Serializable]
    public enum ResearchClientUiKey
    {
        Key,
    }
    // KS14: removed ResearchClientBoundInterfaceState, the server list is networked component state
}
