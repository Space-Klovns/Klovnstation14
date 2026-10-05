namespace Content.Server._KS14.Procedural;

/// <summary>Prevents synchronous preview callbacks from nesting another owned allocation transaction.</summary>
public sealed partial class KsProcgenPreviewOperationSystem : EntitySystem
{
    private object? _owner;

    public bool Active => _owner != null;

    internal bool TryEnter(object owner)
    {
        if (_owner != null)
            return false;
        _owner = owner;
        return true;
    }

    internal void Exit(object owner)
    {
        if (ReferenceEquals(_owner, owner))
            _owner = null;
    }
}
