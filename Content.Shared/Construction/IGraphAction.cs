namespace Content.Shared.Construction
{
    [ImplicitDataDefinitionForInheritors]
    public partial interface IGraphAction
    {
        // TODO pass in node/edge & graph ID for better error logs.
        void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager);

        // KS14
        virtual void Initialize(IEntitySystemManager sysManager)
        {
            sysManager.DependencyCollection.InjectDependencies(this);
        }
    }
}
