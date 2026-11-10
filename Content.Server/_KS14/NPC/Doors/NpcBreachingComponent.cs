using Content.Shared.DoAfter;

namespace Content.Server._KS14.NPC.Doors;

/// <summary>
///     An NPC part-way through forcing a door: the door, the tool, and how to put things back as they were, so that
///         however the breach ends the tool goes back and the weapon comes back out. See
///         <see cref="NpcDoorSystem.TryStartBreach"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcDoorSystem))]
public sealed partial class NpcBreachingComponent : Component
{
    [ViewVariables]
    public EntityUid DoorUid;

    [ViewVariables]
    public EntityUid ToolUid;

    [ViewVariables]
    public NpcBreachMethod Method;

    /// <summary>
    ///     Whether steering started it, on its own, for a door in the way (see
    ///         <see cref="NpcDoorSystem.TryBreachBlockingDoor"/>), rather than an order to. A breach steering started
    ///         ends when the NPC stops going anywhere.
    /// </summary>
    [ViewVariables]
    public bool FromSteering;

    /// <summary>
    ///     What was taken out, from where, and what was in hand before: how to put it all back. See
    ///         <see cref="Hands.NpcHandsSystem"/>.
    /// </summary>
    [ViewVariables]
    public Hands.NpcTakenOut? TakenOut;

    [ViewVariables]
    public DoAfterId? DoAfterId;

    /// <summary>
    ///     When to give up if the door is still shut.
    /// </summary>
    [ViewVariables]
    public TimeSpan GiveUpAt;
}
