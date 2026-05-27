namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Added to something issuing warnings before killing.
/// </summary>
[RegisterComponent]
[AutoGenerateComponentPause]
public sealed partial class NpcRetaliationWarningComponent : Component
{
    /// <summary>
    ///     When the warnings expire, duh.
    /// </summary>
    [AutoPausedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public Dictionary<EntityUid, TimeSpan> ExpiryTimes = [];

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float DistanceThresholdSq = 4f;
}
