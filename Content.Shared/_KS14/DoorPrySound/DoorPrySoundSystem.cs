using Content.Shared.Doors.Components;
using Content.Shared.Prying.Components;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._KS14.DoorPrySound;

public sealed partial class DoorPrySoundSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audioSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorPrySoundComponent, PriedEvent>(OnPried);
    }

    private void OnPried(Entity<DoorPrySoundComponent> entity, ref PriedEvent args)
    {
        if (!TryComp<DoorComponent>(entity, out var doorComponent))
            return;

        // ffs this sucks but whatever
        if (doorComponent.State == DoorState.Opening ||
            doorComponent.State == DoorState.Open)
        {
            _audioSystem.PlayPredicted(entity.Comp.OpenSound, entity, user: args.User);
        }
        else if (doorComponent.State == DoorState.Closing ||
            doorComponent.State == DoorState.Closed)
        {
            _audioSystem.PlayPredicted(entity.Comp.CloseSound, entity, user: args.User);
        }
    }
}
