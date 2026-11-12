using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     Makes its entity emit a <see cref="KsParticlesPrototype"/>. Only these few fields are networked; the particles
///         themselves exist on each client alone, so the component works the same on a client-only entity, a
///         predicted spawn and a server-spawned one. Change it through <see cref="KsSharedParticlesSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(KsSharedParticlesSystem))]
public sealed partial class KsParticlesComponent : Component
{
    /// <summary>
    ///     The effect to emit. Null emits nothing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<KsParticlesPrototype>? Prototype;

    /// <summary>
    ///     Whether new particles spawn. Off, the ones alive live out their lifespans.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Emitting = true;

    /// <summary>
    ///     Multiplied into every particle's colour.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;

    /// <summary>
    ///     Where the emitter sits on its entity, in metres.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2 Offset;

    /// <summary>
    ///     Scales the whole effect: positions, velocities and sizes alike.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Scale = 1f;
}
