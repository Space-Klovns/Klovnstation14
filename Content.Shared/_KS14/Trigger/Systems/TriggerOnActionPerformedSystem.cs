using Content.Shared._KS14.Trigger.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Trigger;

namespace Content.Shared._KS14.Trigger.Systems;

public sealed partial class TriggerOnActionPerformedSystem : TriggerOnXSystem
{
    private static readonly KsTriggerOnActionPerformedBaseEvent ConstantEvent = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerOnActionPerformedComponent, KsTriggerOnActionPerformedEntityTargetActionEvent>(OnAction);
        SubscribeLocalEvent<TriggerOnActionPerformedComponent, KsTriggerOnActionPerformedInstantActionEvent>(OnAction);
        SubscribeLocalEvent<TriggerOnActionPerformedComponent, KsTriggerOnActionPerformedWorldTargetActionEvent>(OnAction);

        SubscribeLocalEvent<TriggerOnActionPerformedComponent, ActionGetEventEvent>(OnActionGetEvent);
        SubscribeLocalEvent<TriggerOnActionPerformedComponent, ActionPerformedEvent>(OnActionPerformed);
    }

    private void OnAction<TEvent>(Entity<TriggerOnActionPerformedComponent> entity, ref TEvent args) where TEvent : BaseActionEvent, IKsTriggerOnActionPerformedAction
    {
        args.Handled = true;
    }

    private void OnActionGetEvent(Entity<TriggerOnActionPerformedComponent> entity, ref ActionGetEventEvent args)
    {
        args.Event = ConstantEvent;
    }

    private void OnActionPerformed(Entity<TriggerOnActionPerformedComponent> entity, ref ActionPerformedEvent args)
    {
        EntityUid? passedUserUid = null;
        if (entity.Comp.UserIsTarget)
        {
            if (args.ActionEvent is EntityTargetActionEvent entityTargetActionEvent)
                passedUserUid = entityTargetActionEvent.Target;
        }
        else
            passedUserUid = args.ActionEvent.Performer;

        Trigger.Trigger(entity, user: passedUserUid, key: entity.Comp.KeyOut, predicted: args.Predicted);
    }
}
