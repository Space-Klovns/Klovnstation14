using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     Where a field commander is, for the status strip of its admin window. Raw values: the window formats them.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminStatusData
{
    public int PhaseIndex;
    public LocId? PhaseName;
    public TimeSpan Elapsed;
    public TimeSpan? NextPhaseIn;
    public TimeSpan? NextWaveIn;
    public int Alive;
    public int Cap;
    public int Defenders;
    public float CarriedBudget;
    public int WavesSent;
    public bool Paused;
    public LocId? DoctrineName;

    /// <summary>
    ///     What is waiting in the queue, in the order it will be sent.
    /// </summary>
    public List<KsFieldCommanderAdminQueuedWaveData> QueuedWaves = new();
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminPhaseData
{
    public LocId Name;
    public TimeSpan Start;
    public TimeSpan WaveInterval;
    public float Budget;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminAssetData
{
    public string? Entity;
    public string? Humanoid;
    public int Count;
    public float Cost;
    public float Weight;
    public int MinPhase;
    public int MaxAlive;

    /// <summary>
    ///     Index into <see cref="KsFieldCommanderAdminEuiState.DeliveryNames"/>.
    /// </summary>
    public byte Delivery;

    public List<string> Tags = new();
    public int GuaranteedEveryWaves;
    public bool GuaranteedOnPhaseEnter;
    public bool Disabled;

    public float StandOff;

    /// <summary>
    ///     Its own points for a kill, or null for the cost's share.
    /// </summary>
    public int? KillPoints;

    public bool GiveOrders;
    public string HumanoidName = string.Empty;
    public string? Announcement;
    public string? Intel;

    /// <summary>
    ///     How many units bought as this are alive now.
    /// </summary>
    public int Living;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminQueuedWaveData
{
    public List<KsFieldCommanderAdminPurchase> Purchases = new();
    public string? Announcement;
}

/// <summary>
///     So many purchases of the asset at this index.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsFieldCommanderAdminPurchase(int Asset, int Count);

/// <summary>
///     Everything the admin window's tabs show. Sent when it opens, after every change made through it, and on
///         request; the status alone is sent more often, by <see cref="KsFieldCommanderAdminStatusMessage"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminEuiState : EuiStateBase
{
    /// <summary>
    ///     The commander shown, or null when none is running. Every message names it back, so that one meant for a
    ///         commander that has since ended is not applied to the next.
    /// </summary>
    public NetEntity? Rule;

    public KsFieldCommanderAdminStatusData Status = new();
    public List<KsFieldCommanderAdminPhaseData> Phases = new();
    public List<KsFieldCommanderAdminAssetData> Assets = new();

    /// <summary>
    ///     Names of the ways an asset can be delivered, by their value.
    /// </summary>
    public List<string> DeliveryNames = new();

    // Limits the server clamps to, for the window's inputs.
    public float MaxBudget;
    public float MaxCost;
    public float MaxWeight;
    public int MaxCount;
    public int MaxAliveLimit;
    public float MaxStandOff;
    public TimeSpan MaxTime;
    public int MaxQueuedPurchases;
    public int MaxAnnouncementLength;
}

/// <summary>
///     The status strip's own refresh, sent every second while the window is open, so that nothing the admin is
///         typing in a tab is written over.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminStatusMessage(NetEntity? rule, KsFieldCommanderAdminStatusData status) : EuiMessageBase
{
    public NetEntity? Rule = rule;
    public KsFieldCommanderAdminStatusData Status = status;
}

/// <summary>
///     Whether the server took the window's last change.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminResultMessage(bool accepted) : EuiMessageBase
{
    public bool Accepted = accepted;
}

/// <summary>
///     A change from the admin window to the commander it names.
/// </summary>
[Serializable, NetSerializable]
public abstract class KsFieldCommanderAdminEuiMessage(NetEntity rule) : EuiMessageBase
{
    public NetEntity Rule = rule;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminRefreshMessage(NetEntity rule) : KsFieldCommanderAdminEuiMessage(rule);

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminSetPausedMessage(NetEntity rule, bool paused) : KsFieldCommanderAdminEuiMessage(rule)
{
    public bool Paused = paused;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminForceWaveMessage(NetEntity rule) : KsFieldCommanderAdminEuiMessage(rule);

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminSetPhaseMessage(NetEntity rule, int phase) : KsFieldCommanderAdminEuiMessage(rule)
{
    public int Phase = phase;
}

/// <summary>
///     Changes a phase; null leaves a value as it is.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminEditPhaseMessage(NetEntity rule, int phase, TimeSpan? start, TimeSpan? waveInterval, float? budget)
    : KsFieldCommanderAdminEuiMessage(rule)
{
    public int Phase = phase;
    public TimeSpan? Start = start;
    public TimeSpan? WaveInterval = waveInterval;
    public float? Budget = budget;
}

/// <summary>
///     An asset's values; null leaves one as it is.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct KsFieldCommanderAdminAssetValues(
    string? Entity = null,
    string? Humanoid = null,
    int? Count = null,
    float? Cost = null,
    float? Weight = null,
    int? MinPhase = null,
    int? MaxAlive = null,
    byte? Delivery = null,
    int? GuaranteedEveryWaves = null,
    bool? GuaranteedOnPhaseEnter = null,
    List<string>? Tags = null,
    float? StandOff = null,
    int? KillPoints = null,
    bool? GiveOrders = null,
    string? HumanoidName = null,
    string? Announcement = null,
    string? Intel = null);

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminEditAssetMessage(NetEntity rule, int asset, KsFieldCommanderAdminAssetValues values)
    : KsFieldCommanderAdminEuiMessage(rule)
{
    public int Asset = asset;
    public KsFieldCommanderAdminAssetValues Values = values;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminAddAssetMessage(NetEntity rule, KsFieldCommanderAdminAssetValues values)
    : KsFieldCommanderAdminEuiMessage(rule)
{
    public KsFieldCommanderAdminAssetValues Values = values;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminSetAssetDisabledMessage(NetEntity rule, int asset, bool disabled)
    : KsFieldCommanderAdminEuiMessage(rule)
{
    public int Asset = asset;
    public bool Disabled = disabled;
}

[Serializable, NetSerializable]
public sealed class KsFieldCommanderAdminQueueWaveMessage(NetEntity rule, List<KsFieldCommanderAdminPurchase> purchases, string? announcement, bool sendNow)
    : KsFieldCommanderAdminEuiMessage(rule)
{
    public List<KsFieldCommanderAdminPurchase> Purchases = purchases;
    public string? Announcement = announcement;
    public bool SendNow = sendNow;
}
