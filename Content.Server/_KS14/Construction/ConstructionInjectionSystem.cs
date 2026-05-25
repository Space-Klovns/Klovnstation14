using Content.Shared._KS14.IoC;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Construction;

/// <summary>
///     AAAAAAAAAAAGGGGGH!!!!!!
/// </summary>
public sealed class ConstructionInjectionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _collectionHook = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypeLoad);
        _collectionHook.HookAction(OnLoad);
    }

    private void OnLoad()
    {
        foreach (var graphPrototype in _prototypeManager.EnumeratePrototypes<ConstructionGraphPrototype>())
        {
            foreach (var (_, node) in graphPrototype.Nodes)
            {
                EnumerateActions(node.Actions);
                foreach (var edge in node.Edges)
                {
                    EnumerateConditions(edge.Conditions);
                    EnumerateActions(edge.Completed);

                    foreach (var step in edge.Steps)
                        EnumerateActions(step.Completed);
                }
            }
        }
    }

    private void EnumerateActions(IEnumerable<IGraphAction> things)
    {
        foreach (var act in things)
            act.Initialize(EntityManager.EntitySysManager);
    }

    private void EnumerateConditions(IEnumerable<IGraphCondition> things)
    {
        foreach (var act in things)
            act.Initialize(EntityManager.EntitySysManager);
    }

    private void OnPrototypeLoad(PrototypesReloadedEventArgs obj)
    {
        OnLoad();
    }
}
