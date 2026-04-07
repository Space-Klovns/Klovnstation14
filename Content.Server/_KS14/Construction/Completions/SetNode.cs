using Content.Shared.Construction;
using Content.Server.Construction;
using Content.Server.Construction.Components;

namespace Content.Server._KS14.Construction.Completions;

/// <summary>
///     Naively sets current node of the construction graph.
/// </summary>
[DataDefinition]
public sealed partial class SetNode : IGraphAction
{
    [Dependency] private readonly ConstructionSystem _constructionSystem = default!;

    /// <summary>
    ///     Node ID.
    /// </summary>
    [DataField(required: true)] public string Node = "";
    [DataField] public bool PerformActions = true;

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        if (!entityManager.TryGetComponent<ConstructionComponent>(uid, out var constructionComponent))
            return;

        _constructionSystem.ChangeNode(uid, userUid, Node, performActions: PerformActions, constructionComponent);
        _constructionSystem.ResetEdge(uid, construction: constructionComponent);
    }
}
