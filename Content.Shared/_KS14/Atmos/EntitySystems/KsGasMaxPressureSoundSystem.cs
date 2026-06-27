using Content.Shared._KS14.Atmos.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._KS14.Atmos.EntitySystems;

public sealed class KsGasMaxPressureSoundSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsGasMaxPressureSoundComponent, KsGasMaxPressureAfterIntegrityLostEvent>(OnAfterLoseIntegrity);
    }

    private void OnAfterLoseIntegrity(Entity<KsGasMaxPressureSoundComponent> entity, ref KsGasMaxPressureAfterIntegrityLostEvent args)
    {
        _audioSystem.PlayPvs(entity.Comp.OverpressureSound, entity.Owner);

        if (args.Component.Integrity <= entity.Comp.FinalOverpressureThreshold)
            _audioSystem.PlayPvs(entity.Comp.FinalOverpressureSound, entity.Owner);
    }
}
