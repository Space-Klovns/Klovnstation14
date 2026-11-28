using Content.Shared.Actions.Components;

namespace Content.Shared.Actions;

public abstract partial class SharedActionsSystem
{
    /// <summary>
    ///     Whether the action belongs on the action bar. An action that doesn't stays attached and usable (from a menu,
    ///         say) but the client keeps it off the bar, and puts it on or takes it off when this changes.
    /// </summary>
    public void SetAutoPopulate(Entity<ActionComponent?> ent, bool autoPopulate)
    {
        if (!_actionQuery.Resolve(ent, ref ent.Comp) || ent.Comp.AutoPopulate == autoPopulate)
            return;

        ent.Comp.AutoPopulate = autoPopulate;
        DirtyField(ent, ent.Comp, nameof(ActionComponent.AutoPopulate));
    }

    /// <summary>
    ///     How far away a targeted action can be aimed.
    /// </summary>
    public void SetTargetRange(Entity<TargetActionComponent?> ent, float range)
    {
        if (!Resolve(ent, ref ent.Comp, false) || ent.Comp.Range.Equals(range))
            return;

        ent.Comp.Range = range;
        Dirty(ent, ent.Comp);
    }
}
