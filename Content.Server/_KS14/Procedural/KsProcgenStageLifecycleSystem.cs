using System.Threading;
using Robust.Shared.Map;

namespace Content.Server._KS14.Procedural;

internal enum KsProcgenStageInitializationStatus : byte
{
    Initialized, InvalidInput, Rejected, BudgetExceeded, Cancelled, EngineFailure,
}

internal sealed record KsProcgenStageInitializationResult(KsProcgenStageInitializationStatus Status, string? IssueCode);

/// <summary>
/// One synchronous, paused initialization transaction. Kind-specific inspectors are transient
/// arguments, never retained session state. This does not isolate global initializer side effects
/// or authorize simulation, network visibility, utility operation or publication.
/// </summary>
public sealed partial class KsProcgenStageLifecycleSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private KsProcgenStageOwnershipSystem _ownership = default!;
    [Dependency] private KsProcgenPreviewOperationSystem _operations = default!;

    internal KsProcgenStageInitializationResult TryInitialize(KsProcgenStageHandle stage, string prefix,
        Func<bool, string?> inspect, CancellationToken cancellationToken)
    {
        KsProcgenStageInitializationResult Fail(KsProcgenStageInitializationStatus status, string code) => new(status, code);
        if (!_ownership.Owns(stage))
            return Fail(KsProcgenStageInitializationStatus.InvalidInput, prefix + "NotOwned");
        if (stage.Kind == KsProcgenStageKind.Tiles)
            return Fail(KsProcgenStageInitializationStatus.InvalidInput, prefix + "InitializationUnsupported");
        if (!_operations.TryEnter(this))
            return Fail(KsProcgenStageInitializationStatus.InvalidInput, prefix + "OperationActive");
        var retained = false;
        try
        {
            _operations.SetCapture(this, stage.SpawnedEntities);
            cancellationToken.ThrowIfCancellationRequested();
            var precondition = inspect(false);
            if (precondition != null)
                return Fail(KsProcgenStageInitializationStatus.Rejected, precondition);
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.InitializationPhase == KsProcgenStageInitializationPhase.Initialized)
            {
                retained = true;
                return new(KsProcgenStageInitializationStatus.Initialized, null);
            }
            if (stage.InitializationPhase != KsProcgenStageInitializationPhase.Uninitialized)
                return Fail(KsProcgenStageInitializationStatus.Rejected, prefix + "InitializationPhaseDrift");
            _maps.SetPaused(stage.MapId, true);
            stage.InitializationPhase = KsProcgenStageInitializationPhase.Initializing;
            _maps.InitializeMap(stage.MapId, unpause: false);
            // An initializer can retire the ownership component. Never revive that session or
            // certify completion on a replacement map, but still clean late recorded allocations.
            if (_ownership.Owns(stage))
                stage.InitializationPhase = KsProcgenStageInitializationPhase.Initialized;
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.SpawnedEntityCount > stage.MaxSpawnedEntities)
                return Fail(KsProcgenStageInitializationStatus.BudgetExceeded, prefix + "InitializationSpawnBudget");
            var verification = inspect(true);
            if (verification != null)
                return Fail(KsProcgenStageInitializationStatus.Rejected, verification);
            cancellationToken.ThrowIfCancellationRequested();
            retained = true;
            return new(KsProcgenStageInitializationStatus.Initialized, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(KsProcgenStageInitializationStatus.Cancelled, prefix + "InitializationCancelled");
        }
        catch (Exception)
        {
            return Fail(KsProcgenStageInitializationStatus.EngineFailure, prefix + "InitializationEngineFailure");
        }
        finally
        {
            _operations.SetCapture(this, null);
            try
            {
                // Exactly one cleanup attempt per failed transaction, even when inspection or
                // initialization threw. Already-retired retained handles may own late allocations.
                if (!retained) _ownership.Release(stage);
            }
            finally { _operations.Exit(this); }
        }
    }
}
