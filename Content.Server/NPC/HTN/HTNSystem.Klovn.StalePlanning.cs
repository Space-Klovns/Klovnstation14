// KS14: added in this fork
namespace Content.Server.NPC.HTN;

public sealed partial class HTNSystem
{
    /// <summary>
    ///     The running plan has just ended, finished or failed. A plan already being worked out was worked out against
    ///         the blackboard as it was when it was asked for - at the top of this update, before the plan that just ended
    ///         ran - and so knows nothing of what that plan changed. With no plan left to compare it against, it would be
    ///         taken as it is: a plan that removes a marker and says a line, worked out again from before the marker was
    ///         removed, says the line twice. So it is dropped, and the next update asks again from the blackboard as it is
    ///         now.
    /// </summary>
    private void KsDiscardStalePlanning(HTNComponent component)
    {
        if (component.PlanningJob == null)
            return;

        component.PlanningToken?.Cancel();
        component.PlanningToken = null;
        component.PlanningJob = null;
        component.PlanAccumulator = 0f;
    }
}
