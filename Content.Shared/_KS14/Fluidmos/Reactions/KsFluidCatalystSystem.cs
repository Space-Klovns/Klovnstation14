using Content.Shared.Examine;

namespace Content.Shared._KS14.Fluidmos.Reactions;

/// <summary>
///     Shows a catalyst bed's activity and wears it down.
/// </summary>
public sealed partial class KsFluidCatalystSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsFluidCatalystComponent> entity, ref ExaminedEvent args)
    {
        var catalyst = ProtoMan.Index(entity.Comp.Catalyst);
        args.PushMarkup(Loc.GetString("ks-fluid-catalyst-examine",
            ("catalyst", Loc.GetString(catalyst.Name)),
            ("activity", (int)MathF.Round(entity.Comp.Activity * 100f))));
    }

    /// <summary>
    ///     Takes <paramref name="activityLost"/> off the bed's activity, never below zero.
    /// </summary>
    public void Poison(Entity<KsFluidCatalystComponent> entity, float activityLost)
    {
        if (activityLost <= 0f || entity.Comp.Activity <= 0f)
            return;

        entity.Comp.Activity = MathF.Max(0f, entity.Comp.Activity - activityLost);
        Dirty(entity);
    }
}
