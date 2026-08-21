using Content.Shared._KS14.Anchorless.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Actions;
using Content.Shared.RetractableItemAction;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Anchorless.Systems;

/// <summary>
/// Horror form is a first-class form state. It never creates a hidden mob or a second body.
/// </summary>
public sealed partial class AnchorlessHorrorSystem : EntitySystem
{
    private static readonly EntProtoId HorrorArmbladeAction = "ActionAnchorlessHorrorArmblade";

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAnchorlessIdentitySystem _identities = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KsAnchorlessAntagComponent, AnchorlessHorrorActionEvent>(OnHorror);
        SubscribeLocalEvent<KsAnchorlessAntagComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(Entity<KsAnchorlessAntagComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Damage.DamageDict.TryGetValue("Heat", out var heat))
            args.Damage.DamageDict["Heat"] = heat * ent.Comp.HeatMultiplier;
    }
    private void OnHorror(Entity<KsAnchorlessAntagComponent> ent, ref AnchorlessHorrorActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!ent.Comp.HorrorForm)
        {
            _actions.AddAction(ent.Owner, ref ent.Comp.HorrorArmbladeAction, HorrorArmbladeAction, ent.Owner);
            ent.Comp.HorrorForm = true;
        }
        else
        {
            // Restoring the current stored identity uses the standard Anchorless transform
            // path, including its component cloning safeguards for inventory and storage.
            var target = ent.Comp.CurrentIdentity;
            if (target == null)
                return;

            RemoveArmblade(ent);
            _identities.TransformInto(ent, target.Value);
            ent.Comp.HorrorForm = false;
        }

        Dirty(ent);
    }

    private void RemoveArmblade(Entity<KsAnchorlessAntagComponent> ent)
    {
        if (ent.Comp.HorrorArmbladeAction is not { } action)
            return;

        if (TryComp<RetractableItemActionComponent>(action, out var retract) && retract.ActionItemUid is { } item)
            QueueDel(item);

        _actions.RemoveAction(ent.Owner, action);
        ent.Comp.HorrorArmbladeAction = null;
    }
}
