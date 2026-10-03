using Content.Server.NPC.HTN.PrimitiveTasks.Operators;
using Content.Server.NPC.Systems;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     The NPC is moving in the background: a <see cref="MoveToOperator"/> that keeps steering after its task has
///         finished (<c>shutdownState</c> <c>PlanFinished</c> or <c>Never</c>) has handed the move off, and later tasks
///         - shooting, reloading, healing - are running alongside it with the NPC's hands. Anything steering would do
///         with those hands on its own, like forcing a door in the way, waits for a move that is the task at hand.
///         Cleared when steering is registered afresh or stops.
/// </summary>
[RegisterComponent]
[Access(typeof(MoveToOperator), typeof(NPCSteeringSystem))]
public sealed partial class NpcBackgroundMoveComponent : Component;
