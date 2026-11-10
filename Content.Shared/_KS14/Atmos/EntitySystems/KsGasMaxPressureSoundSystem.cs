using Content.Shared._KS14.Atmos.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._KS14.Atmos.EntitySystems;

public sealed partial class KsGasMaxPressureSoundSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    [SubscribeLocalEvent]
    private void OnAfterLoseIntegrity(Entity<KsGasMaxPressureSoundComponent> entity, ref KsGasMaxPressureAfterIntegrityLostEvent args)
    {
        _audioSystem.PlayPvs(entity.Comp.OverpressureSound, entity.Owner);

        if (args.Component.Integrity <= entity.Comp.FinalOverpressureThreshold)
            _audioSystem.PlayPvs(entity.Comp.FinalOverpressureSound, entity.Owner);
    }
}
