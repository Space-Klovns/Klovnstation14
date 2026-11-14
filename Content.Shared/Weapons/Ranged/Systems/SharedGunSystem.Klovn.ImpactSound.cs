using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    /// <summary>
    ///     Plays a hit's sound for this client alone. <c>PlayLocal</c> is <c>PlayPredicted</c> on the client, which
    ///         plays nothing on a re-predicted tick; a kept projectile's hit can land on one and happens only once,
    ///         so its sound was lost. With <paramref name="once"/> the caller has made sure this runs once, and it
    ///         plays whatever the tick.
    /// </summary>
    private void KsPlayImpactAudio(SoundSpecifier? sound, EntityUid source, AudioParams? audioParams, bool once)
    {
        if (!once)
        {
            Audio.PlayLocal(sound, source, null, audioParams);
            return;
        }

        if (sound != null)
            Audio.PlayStatic(sound, Filter.Local(), Transform(source).Coordinates, recordReplay: false, audioParams);
    }
}
