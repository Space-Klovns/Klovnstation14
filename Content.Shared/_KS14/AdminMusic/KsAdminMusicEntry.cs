using Robust.Shared.Audio;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.AdminMusic;

public sealed class KsAdminMusicEntry(ResPath soundPath, float volume, TimeSpan startTime) : IEquatable<KsAdminMusicEntry>
{
    public ResPath SoundPath = soundPath;

    /// <inheritdoc cref="AudioParams.Volume"/>
    public float Volume = volume;

    /// <summary>
    ///     In-simulation time that this audio started/starts playing
    ///         at.
    /// </summary>
    public TimeSpan StartTime = startTime;

    bool IEquatable<KsAdminMusicEntry>.Equals(KsAdminMusicEntry? other)
    {
        if (other is not { })
            return false;

        if (other.SoundPath != SoundPath ||
            other.Volume != Volume ||
            other.StartTime != StartTime)
            return false;

        return true;
    }

    public override int GetHashCode()
        => HashCode.Combine(SoundPath, Volume, StartTime);
}
