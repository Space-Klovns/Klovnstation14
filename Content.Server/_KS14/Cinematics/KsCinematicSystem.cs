using Content.Shared._KS14.Cinematics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cinematics;

/// <summary>
///     Starts cinematics on clients. Everything is timed from the prototype alone, so this never needs the
///         cinematic's textures.
/// </summary>
public sealed partial class KsCinematicSystem : EntitySystem
{
    /// <summary>
    ///     Plays a cinematic.
    /// </summary>
    /// <param name="filter">Who sees it. Defaults to every player in game; lobby players have no attached entity.</param>
    /// <param name="stretchUntil">
    ///     When the scene after the cinematic's stretched scene should start, counted from the start of the cinematic.
    ///         It never starts earlier than the prototype's own durations allow.
    /// </param>
    /// <param name="continuedSound">An already playing sound that should carry on through the cinematic.</param>
    /// <returns>
    ///     When the stretched scene ends, or the whole cinematic's length if it has none,
    ///         or null if the cinematic doesn't exist.
    /// </returns>
    public TimeSpan? Play(
        ProtoId<KsCinematicPrototype> cinematicId,
        Filter? filter = null,
        TimeSpan? stretchUntil = null,
        KsCinematicContinuedSound? continuedSound = null)
    {
        if (!ProtoMan.TryIndex(cinematicId, out var cinematic))
        {
            Log.Error($"Tried to play nonexistent cinematic {cinematicId}.");
            return null;
        }

        var sceneDurations = GetSceneDurations(cinematic, stretchUntil, out var stretchEnd);
        RaiseNetworkEvent(new KsPlayCinematicEvent(cinematicId, sceneDurations, continuedSound),
            filter ?? Filter.Empty().AddWhereAttachedEntity(_ => true));

        return stretchEnd;
    }

    private static TimeSpan[] GetSceneDurations(KsCinematicPrototype cinematic, TimeSpan? stretchUntil, out TimeSpan stretchEnd)
    {
        var sceneDurations = new TimeSpan[cinematic.Scenes.Count];
        var elapsed = TimeSpan.Zero;
        TimeSpan? stretchedSceneEnd = null;

        for (var i = 0; i < cinematic.Scenes.Count; i++)
        {
            var scene = cinematic.Scenes[i];
            var duration = scene.Duration;

            if (scene.Stretch && stretchedSceneEnd == null)
            {
                if (stretchUntil is { } until && elapsed + duration < until)
                    duration = until - elapsed;

                stretchedSceneEnd = elapsed + duration;
            }

            sceneDurations[i] = duration;
            elapsed += duration;
        }

        stretchEnd = stretchedSceneEnd ?? elapsed;
        return sceneDurations;
    }
}
