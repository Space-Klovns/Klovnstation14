using System.Linq;
using Content.Shared.Actions.Events;
using Content.Shared.Popups;

namespace Content.Shared._KS14.Actions;

/// <summary>
/// Handles action priming, confirmation and automatic unpriming.
/// </summary>
public sealed partial class KsRadiusRestrictedActionSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsRadiusRestrictedActionComponent, ActionAttemptEvent>(OnActionAttempt);
    }

    private void OnActionAttempt(Entity<KsRadiusRestrictedActionComponent> entity, ref ActionAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (FoundInRange(entity, args.User) ^ entity.Comp.Inverted)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString(entity.Comp.Popup), args.User, args.User, PopupType.MediumCaution);
    }

    private bool FoundInRange(Entity<KsRadiusRestrictedActionComponent> entity, EntityUid userUid)
    {
        if (entity.Comp.Components.Count == 0)
            return false;

        var componentTypes = entity.Comp.Components.Values.Select(componentRegistry => componentRegistry.Component.GetType());

        var entitiesInRange = new HashSet<Entity<IComponent>>();
        _entityLookupSystem.GetEntitiesInRange(componentTypes.First(), _transformSystem.GetMapCoordinates(userUid), entity.Comp.Radius, entitiesInRange);

        foreach (var otherEntity in entitiesInRange)
        {
            if (!entity.Comp.IgnoreUser &&
                otherEntity.Owner == entity.Owner)
                continue;

            var foundAllComps = true;
            foreach (var searchedComponentType in componentTypes)
            {
                if (otherEntity.Comp.GetType() == searchedComponentType)
                    continue;

                if (HasComp(otherEntity.Owner, searchedComponentType))
                    continue;

                foundAllComps = false;
                break;
            }

            if (!foundAllComps)
                continue;

            return true;
        }

        return false;
    }
}
