using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     Sent server -> the toggling client only, telling it whether it now receives <see cref="SquadPlanDebugDataMessage"/>s.
/// </summary>
[Serializable, NetSerializable]
public sealed class SquadPlanDebugStateMessage : EntityEventArgs
{
    public bool Enabled;

    /// <summary>
    ///     Null = every squad; otherwise only this squad, or this NPC's.
    /// </summary>
    public NetEntity? Target;
}

/// <summary>
///     Where one squad's plan stands: what it knows, what it means to do, and what is in the way.
/// </summary>
[Serializable, NetSerializable]
public sealed class SquadPlanDebugDataMessage : EntityEventArgs
{
    /// <summary>
    ///     The squad, or the lone NPC.
    /// </summary>
    public NetEntity Issuer;

    public NpcSquadPlanState State;

    public string Reason = string.Empty;

    public float Elapsed;

    public float Timeout;

    public NpcSquadFact Facts;

    public readonly List<NpcSquadAction> Steps = new();

    public int CurrentStep;

    public float Cost;

    public readonly List<NpcSquadAction> Banned = new();

    /// <summary>
    ///     How far the charge in use has got, where its planter sticks it on, and where it sets it off from. No phase, no charge.
    /// </summary>
    public NpcSquadChargePhase? ChargePhase;

    public NetCoordinates ChargeStand;

    public NetCoordinates ChargeStandOff;

    public NetCoordinates Target;

    public readonly List<SquadPlanDebugShot> Shots = new();

    public readonly List<NetCoordinates> Path = new();

    public readonly List<NetCoordinates> Barriers = new();

    public readonly List<HuntDebugEntrance> Entrances = new();

    public readonly List<SquadDebugOrder> Orders = new();
}

/// <summary>
///     Told to whoever was watching once a squad has no plan any more.
/// </summary>
[Serializable, NetSerializable]
public sealed class SquadPlanDebugEndedMessage : EntityEventArgs
{
    public NetEntity Issuer;
}

/// <param name="From">The member.</param>
/// <param name="To">How far its shot gets: the hostile, or whatever stops it.</param>
/// <param name="Clear">Whether it gets all the way there.</param>
[Serializable, NetSerializable]
public readonly record struct SquadPlanDebugShot(NetCoordinates From, NetCoordinates To, bool Clear);
