using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Packets;

public abstract partial class SharedPacketExecutorComponent : Component
{
    /// <summary>
    /// Currently saved command for next execution(s).
    /// </summary>
    [DataField]
    public string Command = "";

    /// <summary>
    /// Maximum amount of characters command can have to be executed or saved.
    /// </summary>
    [DataField]
    public int MaxCommandLength = 36500;

    /// <summary>
    /// Sound that will be played if execution attempt fails (Due to cooldown or code being too long).
    /// </summary>
    [DataField]
    public SoundSpecifier? ExecutionFailSound;
}
