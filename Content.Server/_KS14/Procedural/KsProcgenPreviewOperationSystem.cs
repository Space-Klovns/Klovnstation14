namespace Content.Server._KS14.Procedural;

/// <summary>Prevents synchronous preview callbacks from nesting another owned allocation transaction.</summary>
public sealed partial class KsProcgenPreviewOperationSystem : EntitySystem
{
    public const int MaximumAllocationsPerStage = 256;
    private object? _owner;
    private List<EntityUid>? _allocations;

    public bool Active => _owner != null;
    public int CapturedEntityCount => _allocations?.Count ?? 0;

    internal bool IsCapturing(List<EntityUid> allocations) =>
        _owner != null && ReferenceEquals(_allocations, allocations);

    public override void Initialize()
    {
        base.Initialize();
        EntityManager.EntityAdded += OnEntityAdded;
    }

    public override void Shutdown()
    {
        EntityManager.EntityAdded -= OnEntityAdded;
        _allocations = null; _owner = null;
        base.Shutdown();
    }

    // Allocation precedes components and transforms. Only record here: never inspect, delete,
    // initialize or throw to enforce a budget inside this event. Callers check at safe boundaries.
    private void OnEntityAdded(Entity<MetaDataComponent> entity) => _allocations?.Add(entity.Owner);

    internal bool TryEnter(object owner)
    {
        if (_owner != null)
            return false;
        _owner = owner;
        _allocations = null;
        return true;
    }

    internal void SetCapture(object owner, List<EntityUid>? allocations)
    {
        if (!ReferenceEquals(_owner, owner)) throw new InvalidOperationException("Only the active preview operation may change allocation capture.");
        _allocations = allocations;
    }

    internal void Exit(object owner)
    {
        if (ReferenceEquals(_owner, owner))
        {
            _allocations = null;
            _owner = null;
        }
    }
}
