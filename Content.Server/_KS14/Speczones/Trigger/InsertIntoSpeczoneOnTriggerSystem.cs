using Content.Shared.Trigger;

namespace Content.Server._KS14.Speczones.Trigger;

public sealed partial class InsertIntoSpeczoneOnTriggerSystem : EntitySystem
{
    [Dependency] private SpeczoneSystem _speczoneSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InsertIntoSpeczoneOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<InsertIntoSpeczoneOnTriggerComponent> entity, ref TriggerEvent args)
    {
        if ((entity.Comp.TargetUser ? args.User : entity.Owner) is not { } insertedUid)
            return;

        _speczoneSystem.TryInsertIntoSpeczone(insertedUid, entity.Comp.SpeczoneId, out _);
        args.Handled = true;
    }
}
