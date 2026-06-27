using System.Numerics;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.EntityProcessor.StackProcessor;

[RegisterComponent, NetworkedComponent]
[Access(typeof(KsStackProcessorSystem))]
public sealed partial class KsStackProcessorComponent : Component
{
    /// <summary>
    ///     Output amount is input amount multiplied by this.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Multiplier = 1f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ProcessingTime;

    /// <summary>
    ///     Dict of stack types, and the entities they will be converted into.
    ///         The converted entities should be stackable.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<ProtoId<StackPrototype>, EntProtoId> Conversions = [];

    /// <summary>
    ///     Dict of things that are being processed, and where they will be outputted relative to the processor.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<EntityUid, Vector2> OutputOffsets = [];

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? AnimationLayerKey = null;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? AnimationState = null;
}

[Serializable, NetSerializable]
public sealed class KsStackProcessorComponentState : ComponentState
{
    public Dictionary<NetEntity, Vector2> OutputOffsets = [];
}

[Serializable, NetSerializable]
public enum KsStackProcessorVisuals : byte { Active }
