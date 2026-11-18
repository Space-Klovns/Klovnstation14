using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Companions;

/// <summary>
///     The companions an entity keeps: each one is spawned beside it (or on it), takes its side, and comes back a while
///         after being destroyed. Worked by the server's <c>KsCompanionSystem</c>; a perk gives it as a component.
/// </summary>
/// <remarks>
///     A companion stays while its owner is critical, dissolves when the owner dies, and returns when the owner is
///         revived. Taking this component away deletes them.
/// </remarks>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class KsCompanionsComponent : Component
{
    [DataField]
    public List<KsCompanionEntry> Companions = new();

    /// <summary>
    ///     The companion out for each entry, by index; null while there is none.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid?> Spawned = new();

    /// <summary>
    ///     When each destroyed entry comes back, by index.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public Dictionary<int, TimeSpan> RespawnTimes = new();
}

[DataDefinition]
public sealed partial class KsCompanionEntry
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public KsCompanionMode Mode = KsCompanionMode.Follow;

    /// <summary>
    ///     <see cref="KsCompanionMode.Attached"/> only: where on the owner it sits, in the owner's frame.
    /// </summary>
    [DataField]
    public Vector2 Offset;

    /// <summary>
    ///     How long after being destroyed it comes back.
    /// </summary>
    [DataField]
    public TimeSpan RespawnDelay = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan MaterialiseDuration = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan DissolveDuration = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     Told to the owner when it is destroyed. Given <c>$companion</c> and <c>$seconds</c>.
    /// </summary>
    [DataField]
    public LocId DestroyedPopup = "ks-companion-destroyed";

    /// <summary>
    ///     Told to the owner when it comes back after being destroyed. Given <c>$companion</c>.
    /// </summary>
    [DataField]
    public LocId ReturnedPopup = "ks-companion-returned";
}

public enum KsCompanionMode : byte
{
    /// <summary>
    ///     Walks after the owner: an NPC whose <c>FollowTarget</c> is set to the owner.
    /// </summary>
    Follow,

    /// <summary>
    ///     Rides on the owner at <see cref="KsCompanionEntry.Offset"/>, parented to it and put back there if moved.
    /// </summary>
    Attached,
}
