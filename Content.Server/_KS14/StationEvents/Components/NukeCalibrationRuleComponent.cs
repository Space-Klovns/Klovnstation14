using Content.Server._KS14.StationEvents.Events;
using Robust.Shared.Audio;

// wizden-april-fools-2025 nuke-calibration -> ks14 port:
namespace Content.Server._KS14.StationEvents.Components;

[RegisterComponent, AutoGenerateComponentPause, Access(typeof(NukeCalibrationRule))]
public sealed partial class NukeCalibrationRuleComponent : Component
{
    /// <summary>
    /// Sound of the announcement to play if automatic disarm of the nuke was unsuccessful.
    /// </summary>
    [DataField]
    public SoundSpecifier AutoDisarmFailedSound = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");
    /// <summary>
    /// Sound of the announcement to play if automatic disarm of the nuke was successful.
    /// </summary>
    [DataField]
    public SoundSpecifier AutoDisarmSuccessSound = new SoundPathSpecifier("/Audio/Misc/notice2.ogg");

    [DataField]
    public EntityUid AffectedStation;
    /// <summary>
    /// The nuke that was '''calibrated'''.
    /// </summary>
    [DataField]
    public EntityUid AffectedNuke;
    [DataField]
    public TimeSpan NukeTimer = TimeSpan.FromSeconds(180);
    [DataField]
    public float AutoDisarmChance = 0.5f;
    /// <summary>
    /// How long after the nuke is armed the first announcement is made.
    /// </summary>
    [DataField]
    public TimeSpan FirstAnnouncementDelay = TimeSpan.FromSeconds(25);
    /// <summary>
    /// When the first announcement is made: <see cref="FirstAnnouncementDelay"/> after the rule started.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? FirstAnnouncementAt;
    [DataField]
    public bool FirstAnnouncementMade = false;
}
