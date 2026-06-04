using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Plumbing.IvPump;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class KsPlumbingIvPumpComponent : Component
{
    /// <summary>
    ///     Whether the pump can inject or draw, or both.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public KsPlumbingIvPumpMode AvailableModes = KsPlumbingIvPumpMode.All;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public KsPlumbingIvPumpMode Mode = KsPlumbingIvPumpMode.Drawing;

    /// <summary>
    ///     Invalid if the entity doesn't exist.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public EntityUid ChainStartUid = EntityUid.Invalid;

    /// <summary>
    ///     Invalid if none.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    [AutoNetworkedField]
    public EntityUid PatientUid = EntityUid.Invalid;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int ChainInbetweenCount = 5;

    /// <summary>
    ///     The name of the solution that will hold fluids to be drawn.
    /// </summary>
    [DataField(required: true), ViewVariables(VVAccess.ReadOnly)]
    public string BufferSolutionName;

    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public string InletNodeName;

    /// <summary>
    ///     Server only for no reason
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Entity<SolutionComponent>? BufferSolutionEntity;

    /// <summary>
    ///     How much can be drawn/transferred per sec.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public FixedPoint2 TransferRate = FixedPoint2.New(5);

    /// <summary>
    ///     Because FixedPoint2 rounds shit up, this is what couldnt be added.
    /// </summary>
    [DataField(serverOnly: true), ViewVariables(VVAccess.ReadOnly)]
    public float RemainingToTransfer = 0f;
}

[RegisterComponent]
public sealed partial class KsPlumbingIvPumpChainStartComponent : Component
{
    [DataField]
    public EntityUid PumpUid = EntityUid.Invalid;
}

[Serializable, NetSerializable]
public enum KsPlumbingIvPumpVisuals : byte
{
    /// <summary>
    ///     Whether the thing is actively working.
    ///         Boolean.
    /// </summary>
    Active,

    /// <summary>
    ///     Whether the pump is injecting reagents,
    ///         or otherwise taking them.
    ///
    ///         Boolean.
    /// </summary>
    Injecting
}

[Flags]
[Serializable, NetSerializable]
public enum KsPlumbingIvPumpMode : byte
{
    All = Injecting | Drawing,

    Injecting = 1 << 0,
    Drawing = 1 << 1,
}
