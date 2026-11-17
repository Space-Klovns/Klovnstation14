using Content.Client._KS14.PredictedSpawning;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Chemistry;

/// <summary>
///     Predicts this client's own vapors: the kept copies of the ones its player sprayed or shot (see
///         <see cref="KsPredictedSpawnSystem"/>). Every other vapor here is the server's, and only drawn.
/// </summary>
public sealed partial class KsVaporSystem : SharedVaporSystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsPredictedSpawnSystem _predictedSpawnSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        // After a kept copy is put back where its last step left it, rather than where the transform lerp drew it.
        UpdatesAfter.Add(typeof(KsPredictedSpawnSystem));
    }

    protected override bool IsSimulatedHere(EntityUid uid) => _predictedSpawnSystem.IsKeptLocally(uid);

    protected override bool CanStopNow => !_gameTiming.ApplyingState;
}
