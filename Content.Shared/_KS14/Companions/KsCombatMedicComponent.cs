using Content.Shared.Chemistry.Components;
using Robust.Shared.Audio;

namespace Content.Shared._KS14.Companions;

/// <summary>
///     A bot that patches up hurt friendlies (mobs of its own NPC faction) near it: it goes to the worst of them, readies
///         its injector with a beep for <see cref="Windup"/>, then injects. Worked by the server's
///         <c>KsCombatMedicSystem</c>; a companion is sent after its patient and back to its owner afterwards.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class KsCombatMedicComponent : Component
{
    /// <summary>
    ///     For a patient who is hurt but not critical.
    /// </summary>
    [DataField]
    public Solution Injection = new();

    /// <summary>
    ///     For a critical patient.
    /// </summary>
    [DataField]
    public Solution CriticalInjection = new();

    /// <summary>
    ///     A patient needs at least this much damage to be treated.
    /// </summary>
    [DataField]
    public float MinDamage = 5f;

    /// <summary>
    ///     How far it looks for patients, in metres.
    /// </summary>
    [DataField]
    public float SeekRange = 7f;

    /// <summary>
    ///     How close it must be to inject, in metres.
    /// </summary>
    [DataField]
    public float InjectRange = 1.5f;

    /// <summary>
    ///     The tell: how long it holds the injector up, beeping, before it injects.
    /// </summary>
    [DataField]
    public TimeSpan Windup = TimeSpan.FromSeconds(0.75);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan ScanInterval = TimeSpan.FromSeconds(0.5);

    [DataField]
    public SoundSpecifier? WindupSound = new SoundPathSpecifier("/Audio/Ambience/Objects/periodic_beep.ogg");

    [DataField]
    public SoundSpecifier? InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");

    /// <summary>
    ///     Who it is treating, if anyone.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? Patient;

    /// <summary>
    ///     When the readied injection goes in; null while none is readied.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan? InjectAt;

    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan NextInject;

    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan NextScan;
}
