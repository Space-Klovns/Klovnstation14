using Content.Server.Spawners.Components;
using Robust.Server.Audio;

namespace Content.Server.Spawners.EntitySystems;

public sealed partial class SpawnerSystem
{
    [Dependency] private AudioSystem _audioSystem = default!;

    private void PlaySpawnSound(EntityUid uid, TimedSpawnerComponent component)
    {
        _audioSystem.PlayPvs(component.Sound, uid);
    }
}
