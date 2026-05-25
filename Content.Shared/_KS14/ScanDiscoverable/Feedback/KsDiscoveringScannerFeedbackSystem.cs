using Content.Shared._KS14.ScanDiscoverable.Base;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._KS14.ScanDiscoverable.Feedback;

public sealed class KsDiscoveringScannerFeedbackSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsDiscoveringScannerFeedbackComponent, KsAfterScanDiscoveringEvent>(OnDiscover);
    }

    private void OnDiscover(Entity<KsDiscoveringScannerFeedbackComponent> entity, ref KsAfterScanDiscoveringEvent args)
    {
        if (entity.Owner != args.InteractUsingEvent.Used)
            return;

        _audioSystem.PlayPredicted(entity.Comp.UseSoundSpecifier, entity.Owner, args.InteractUsingEvent.User);
    }
}
