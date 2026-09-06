using Content.Shared.Trigger;

namespace Content.Shared._KS14.Klovnmed.Dismemberment;

public sealed partial class DismemberOnTriggerSystem : EntitySystem
{
    [Dependency] private DismembermentSystem _dismembermentSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DismemberOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<DismemberOnTriggerComponent> entity, ref TriggerEvent args)
    {
        if ((entity.Comp.TargetUser ? args.User : entity.Owner) is not { } targetUid)
            return;

        args.Handled |= _dismembermentSystem.TryDismemberRandomBodyPartOfType(
            targetUid,
            entity.Comp.PartType,
            out _,
            throwSpeed: entity.Comp.ThrowSpeed,
            cause: args.Predicted ? args.User : null
        );
    }
}
