using Content.Shared.HealthExaminable;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body;

public sealed partial class BodySystem : EntitySystem
{

    public void InitializeKlovn()
    {
        SubscribeLocalEvent<BodyComponent, HealthBeingExaminedEvent>(OnHealthBeingExamined);
    }

    private void OnHealthBeingExamined(Entity<BodyComponent> entity, ref HealthBeingExaminedEvent args)
    {
        if (!TryComp<InitialBodyComponent>(entity, out var initialBodyComponent))
            return;

        var presentCategories = new HashSet<ProtoId<OrganCategoryPrototype>>();
        foreach (var childUid in entity.Comp.RecursiveChildUids)
        {
            if (_organQuery.GetComponent(childUid).Category is not { } category)
                continue;

            presentCategories.Add(category);
        }

        var first = false;
        foreach (var requiredCategory in initialBodyComponent.TotalCategories)
        {
            if (presentCategories.Contains(requiredCategory))
                continue;

            if (!Loc.TryGetString("body-component-dismemberedcategory-" + requiredCategory.Id, out var categoryLoc))
                continue;

            // add an extra newline b4 everything
            if (!first)
            {
                first = true;
                args.Message.PushNewline();
            }

            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("body-component-dismembered", ("target", entity.Owner), ("category", categoryLoc)));
        }
    }
}
