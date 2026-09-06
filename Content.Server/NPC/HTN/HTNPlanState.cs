namespace Content.Server.NPC.HTN;

[Flags]
public enum HTNPlanState : byte
{
    // KS14: ANK: start
    /// <summary>
    ///     Very special case. When specified in <see cref="IHtnConditionalShutdown.ShutdownState"/> , this means that
    ///         <see cref="IHtnConditionalShutdown.ConditionalShutdown(NPCBlackboard)"/> will <b>never</b> be called when
    ///         task finished/plan finished.
    ///
    ///     This can be used, for example, in MoveToOperator to make an NPC move to a fixed position while still doing other things
    ///         in future plans, even when this position may change or something.
    /// </summary>
    Never = 0,
    // KS14: ANK: end
    TaskFinished = 1 << 0,

    PlanFinished = 1 << 1,
}
