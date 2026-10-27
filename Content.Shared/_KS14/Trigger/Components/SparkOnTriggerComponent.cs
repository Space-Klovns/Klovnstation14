using System.Numerics;
using Content.Shared._KS14.Sparks;
using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Trigger.Components;

/// <summary>
///     Spawns sparks upon being triggered.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class SparkOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     Range of minimum and maximum number of sparks to emit upon trigger.
    /// </summary>
    [DataField]
    public Vector2i CountRange = new(3, 5);

    /// <summary>
    ///     Range of minimum and maximum velocity of sparks emitted upon trigger.
    /// </summary>
    [DataField]
    public Vector2 VelocityRange = new(2.5f, 5f);

    /// <summary>
    ///     Prototype to spawn as sparks.
    /// </summary>
    [DataField]
    public EntProtoId Prototype = SharedSparksSystem.DefaultSparkPrototype;

    /// <summary>
    ///     Sound to play upon trigger, at position of sparks.
    /// </summary>
    [DataField]
    public SoundSpecifier? SoundSpecifier = SharedSparksSystem.DefaultSoundSpecifier;

    /// <summary>
    ///     Temperature the sparks expose the tile they come from to, which can ignite what's in its air.
    ///         Null for sparks that light nothing.
    /// </summary>
    [DataField]
    public float? ExposedTemperature = 1600f;

    /// <summary>
    ///     Volume of air the sparks expose, see <see cref="ExposedTemperature"/>.
    /// </summary>
    [DataField]
    public float ExposedVolume = 0.6f;
}
