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

        args.Message.PushNewline();
        var allOkay = true;

        foreach (var requiredCategory in initialBodyComponent.TotalCategories)
        {
            if (presentCategories.Contains(requiredCategory))
                continue;

            if (!Loc.TryGetString("ks-body-component-dismemberedcategory-" + requiredCategory.Id, out var categoryLoc))
                continue;

            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("ks-body-component-dismembered", ("target", entity.Owner), ("category", categoryLoc)));

            // FUCK
            allOkay = false;
        }

        if (allOkay)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("ks-body-component-limbs-fine", ("target", entity.Owner)));
        }
    }
}
