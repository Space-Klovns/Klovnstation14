using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Nutrition.Components;

namespace Content.Shared.Nutrition.EntitySystems;

public sealed partial class IngestionSystem
{
    private KsPopupMessage GetEdiblePopupVerb(Entity<EdibleComponent?> entity)
    {
        if (Resolve(entity, ref entity.Comp, false))
            return KsPopupMessage.Create(ProtoMan.Index(entity.Comp.Edible).Verb);
        var ev = new GetEdibleTypeEvent();
        RaiseLocalEvent(entity, ref ev);
        return KsPopupMessage.Create(ev.Type == null ? "edible-verb-edible" : ProtoMan.Index(ev.Type.Value).Verb);
    }
}
