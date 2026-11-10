using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Cinematics;

/// <summary>
///     A full-screen cinematic: a sequence of RSI states played over every bit of a client's screen.
/// </summary>
/// <remarks>
///     All timing lives here rather than in the RSI, because the server times cinematics without loading
///         textures, which a live server may not have.
/// </remarks>
[Prototype]
public sealed partial class KsCinematicPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<KsCinematicPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    /// <summary>
    ///     The RSI the scenes' states come from, relative to <c>/Textures</c>.
    /// </summary>
    [DataField]
    public ResPath Sprite;

    [DataField]
    public TimeSpan FadeIn = TimeSpan.FromSeconds(0.1);

    [DataField]
    public TimeSpan FadeOut = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Whether every sound but the cinematic's own (and its continued sound) is silenced while it plays.
    /// </summary>
    [DataField]
    public bool SilenceAudio = true;

    [DataField]
    public List<KsCinematicScene> Scenes = new();
}

/// <summary>
///     One shot of a <see cref="KsCinematicPrototype"/>.
/// </summary>
[DataDefinition]
public sealed partial class KsCinematicScene
{
    [DataField(required: true)]
    public string State = string.Empty;

    /// <summary>
    ///     How long the scene is shown. An animated state is stretched or squashed to fit, so set this to the
    ///         state's animation length to play it at its own frame timings.
    /// </summary>
    [DataField(required: true)]
    public TimeSpan Duration;

    /// <summary>
    ///     Whether whoever plays the cinematic may lengthen this scene, so that the scene after it starts at a time
    ///         of their choosing. <see cref="Duration"/> is then the minimum. Only the first such scene is stretched.
    /// </summary>
    [DataField]
    public bool Stretch;

    /// <summary>
    ///     Played, never silenced, during the scene.
    /// </summary>
    [DataField]
    public List<KsCinematicSceneSound> Sounds = new();
}

[DataDefinition]
public sealed partial class KsCinematicSceneSound
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>
    ///     How long after the scene starts the sound plays. A sound past the scene's end plays during the next one.
    /// </summary>
    [DataField]
    public TimeSpan Offset;
}
