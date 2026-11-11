using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Construction;

public static class KsPrototypeInheritance
{
    /// <summary>
    ///     Whether <paramref name="uid"/> was spawned from <paramref name="ancestor"/> or from a prototype inheriting
    ///         from it. tgstation's <c>istype()</c>.
    /// </summary>
    public static bool IsOrInheritsFrom(this IPrototypeManager prototypeManager, IEntityManager entityManager, EntityUid uid, EntProtoId ancestor)
    {
        if (entityManager.GetComponent<MetaDataComponent>(uid).EntityPrototype is not { } entityPrototype)
            return false;

        foreach (var (parentId, _) in prototypeManager.EnumerateAllParents<EntityPrototype>(entityPrototype.ID, includeSelf: true))
        {
            if (parentId == ancestor.Id)
                return true;
        }

        return false;
    }
}
