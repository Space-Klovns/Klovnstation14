using Content.Server.AlertLevel;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Changes the sender station's alert level, to one of <see cref="AllowedLevels"/> only.
/// </summary>
public sealed partial class KsLlmAlertLevelToolEffect : KsLlmToolEffect
{
    [DataField]
    public string LevelArgument = "level";

    /// <summary>
    ///     The only levels the model may set. Anything else is refused, whatever the station defines.
    /// </summary>
    [DataField(required: true)]
    public List<string> AllowedLevels = new();

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        var entityManager = context.EntityManager;
        if (context.StationUid is not { } stationUid
            || !entityManager.TryGetComponent<AlertLevelComponent>(stationUid, out var alertLevelComponent)
            || alertLevelComponent.AlertLevels == null)
            return KsLlmToolOutcome.Error("the sending fax's station has no alert levels.");

        var level = context.Arguments.GetString(LevelArgument) ?? string.Empty;
        var allowedLevel = AllowedLevels.Find(allowed => string.Equals(allowed, level, StringComparison.OrdinalIgnoreCase));
        if (allowedLevel == null || !alertLevelComponent.AlertLevels.Levels.ContainsKey(allowedLevel))
            return KsLlmToolOutcome.Error($"'{level}' is not a level you may set. Allowed: {string.Join(", ", AllowedLevels)}.");

        if (alertLevelComponent.IsLevelLocked)
            return KsLlmToolOutcome.Error("the station's alert level is locked and cannot be changed.");

        if (alertLevelComponent.CurrentLevel == allowedLevel)
            return KsLlmToolOutcome.Ok($"The alert level is already {allowedLevel}.");

        var alertLevelSystem = entityManager.System<AlertLevelSystem>();

        // Forced: Central Command is not held to the crew's change delay. Locks were checked above.
        alertLevelSystem.SetLevel(stationUid, allowedLevel, playSound: true, announce: true, force: true);

        return alertLevelSystem.GetLevel(stationUid) == allowedLevel
            ? KsLlmToolOutcome.Ok($"The alert level is now {allowedLevel}.")
            : KsLlmToolOutcome.Error("the alert level could not be changed.");
    }
}
