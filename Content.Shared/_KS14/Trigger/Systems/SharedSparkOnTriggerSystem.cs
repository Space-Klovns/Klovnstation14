using Content.Shared._KS14.Trigger.Components.Effects;
using Content.Shared.Trigger;

namespace Content.Shared._KS14.Sparks;

public sealed class SparkOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedSparksSystem _sparksSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SparkOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<SparkOnTriggerComponent> entity, ref TriggerEvent args)
    {
        var (uid, component) = entity;
        var targetUid = component.TargetUser ? args.User : uid;
        if (!TryComp(targetUid, out TransformComponent? targetTransform))
            return;

        _sparksSystem.DoSparks(
            targetTransform.Coordinates,
            component.Prototype,
            component.SoundSpecifier,
            component.CountRange.X,
            component.CountRange.Y,
            component.VelocityRange.X,
            component.VelocityRange.Y,
            args.User
        );
    }
}
