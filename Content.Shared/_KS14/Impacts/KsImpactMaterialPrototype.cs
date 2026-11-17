using Content.Shared._KS14.Particles;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     What a bullet's impact on a surface looks and sounds like: TGMC's debris element (<c>/datum/element/debris</c>),
///         one of its <c>DEBRIS_*</c> kinds. A surface names one with <see cref="KsImpactSurfaceComponent"/>.
/// </summary>
[Prototype]
public sealed partial class KsImpactMaterialPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Thrown back out of the surface by a ballistic hit. Energy shots throw none, as in TGMC.
    /// </summary>
    [DataField]
    public ProtoId<KsParticlesPrototype>? Debris;

    /// <summary>
    ///     Thrown alongside <see cref="Debris"/>, the same way (armour: its chips, and a few sparks).
    /// </summary>
    [DataField]
    public List<ProtoId<KsParticlesPrototype>> ExtraDebris = new();

    /// <summary>
    ///     Puffed out of the surface by every hit.
    /// </summary>
    [DataField]
    public ProtoId<KsParticlesPrototype>? Smoke = "KsImpactSmoke";

    /// <summary>
    ///     Played by a ballistic hit, with <see cref="SoundChance"/>: TGMC's <c>sound_bounce</c>.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public float SoundChance = 0.75f;

    /// <summary>
    ///     The chance a hit throws the full burst: TGMC's, as <see cref="Debris"/> and <see cref="Smoke"/> are written.
    ///         Every other hit is a lighter one, <see cref="LightAmount"/> as many particles at <see cref="LightSpeed"/>
    ///         times the speed, so a burst of fire reads as a few chips with the odd shower among them.
    /// </summary>
    [DataField]
    public float DenseChance = 0.25f;

    /// <summary>
    ///     How many of the prototypes' particles a light hit throws.
    /// </summary>
    [DataField]
    public float LightAmount = 0.6f;

    /// <summary>
    ///     How fast, against the prototypes' velocities, a light hit's particles fly.
    /// </summary>
    [DataField]
    public float LightSpeed = 0.4f;
}
