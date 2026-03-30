using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.LinkedChain;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class LinkedChainStartComponent : Component
{
    /// <summary>
    ///     Trigger keys in that will be propagated to the end.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> KeysIn = new() { "trigger" };

    /// <summary>
    ///     Ent prototype to spawn for segments.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId SegmentId = "";

    /// <summary>
    ///     Offset of segment joints.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float SegmentOffset = 0.4f;

    /// <summary>
    ///     Number of segments.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public int Segments = 1;

    /// <summary>
    ///     End of the chain.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? EndUid = null;
}

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class LinkedChainEndComponent : Component
{
    /// <summary>
    ///     Start of the chain.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? StartUid = null;
}
