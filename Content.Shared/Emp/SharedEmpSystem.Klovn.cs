using Content.Shared._KS14.PredictedSpawning;

namespace Content.Shared.Emp;

public abstract partial class SharedEmpSystem
{
    [Dependency] private KsSharedPredictedSpawnSystem _ksPredictedSpawnSystem = default!;
}
