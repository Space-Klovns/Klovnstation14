namespace Content.Shared._KS14.FieldGenerator;

/// <summary>
///     Handles removing generated fields that are being shut down,
///         from their generators.
/// </summary>
public sealed partial class KsGeneratedFieldSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KsGeneratedFieldComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<KsGeneratedFieldComponent> entity, ref ComponentShutdown args)
    {
        foreach (var generatorUid in entity.Comp.GeneratorUids)
        {
            if (!TryComp<KsFieldGeneratorComponent>(generatorUid, out var generatorComponent))
                continue;

            generatorComponent.FieldUids.Remove(entity.Owner);
        }
    }
}
