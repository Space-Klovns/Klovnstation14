using Content.Shared.HealthExaminable;
using JetBrains.Annotations;
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

        var allOkay = true;

        foreach (var requiredCategory in initialBodyComponent.TotalCategories)
        {
            if (presentCategories.Contains(requiredCategory))
                continue;

            if (!Loc.TryGetString("ks-body-component-dismemberedcategory-" + requiredCategory.Id, out var categoryLoc))
                continue;

            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("ks-body-component-dismembered", ("target", entity.Owner), ("category", categoryLoc)));

            // FUCK SOMETHING IS MISSING
            allOkay = false;
        }

        if (allOkay)
        {
            args.Message.PushNewline();
            args.Message.AddMarkupOrThrow(Loc.GetString("ks-body-component-limbs-fine"));
        }
    }

    /// <summary>
    ///     Relays the given event to organs within an entity that may have a body.
    /// </summary>
    /// <inheritdoc cref="RelayEvent{T}(Entity{BodyComponent}, ref T)"/>
    [PublicAPI]
    public void RelayEventNullable<T>(Entity<BodyComponent?> ent, ref T args) where T : struct
    {
        if (!_bodyQuery.Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        RelayEvent(ent!, ref args);
    }

    /// <inheritdoc cref="RelayEventNullable{T}(Entity{BodyComponent?}, ref T)"/>
    [PublicAPI]
    public void RelayEventNullable<T>(Entity<BodyComponent?> ent, T args) where T : class
    {
        if (!_bodyQuery.Resolve(ent, ref ent.Comp, logMissing: false))
            return;

        RelayEvent(ent!, args);
    }
}
