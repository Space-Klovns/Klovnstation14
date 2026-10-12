using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Audio;

/// <summary>
///     Marks an audio entity whose auxiliary was set by <see cref="AudioEffectSystem"/>.
///
///     The client's audio system never hands the auxiliary of a newly-received audio entity to its
///         audio source, so without this the effect is networked but never heard. The client re-applies
///         it for every entity carrying this component.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(AudioEffectSystem))]
public sealed partial class AudioEffectAppliedComponent : Component;
