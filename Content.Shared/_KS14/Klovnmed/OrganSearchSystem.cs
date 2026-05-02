using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body;
using Robust.Shared.Collections;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.Klovnmed;

/// <summary>
///     All methods that search for organs here assume that
///         all organs will only either be contained in either a Body,
///         or another organ.
/// </summary>
public sealed class OrganSearchSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public static BodyPartType GetPartType(ProtoId<OrganCategoryPrototype>? protoId)
    {
        if (protoId == null)
            return BodyPartType.Other;

        // fml

        if (protoId == "Torso")
            return BodyPartType.Head;

        if (protoId == "Head")
            return BodyPartType.Head;

        if (protoId == "LegLeft" ||
            protoId == "LegRight")
            return BodyPartType.Leg;

        if (protoId == "FootLeft" ||
            protoId == "FootRight")
            return BodyPartType.Foot;

        if (protoId == "HandLeft" ||
            protoId == "HandRight")
            return BodyPartType.Hand;

        if (protoId == "ArmLeft" ||
            protoId == "ArmRight")
            return BodyPartType.Arm;

        return BodyPartType.Other;
    }

    /// <summary>
    ///     Tries to get a random bodypart by searching recursively, returns
    ///         false if none was found.
    ///
    ///     Supports <see cref="partType"/> having more than one bit set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetRandomBodyPartOfType(Entity<BodyComponent> bodyUid, BodyPartType partType, [NotNullWhen(true)] out EntityUid? partUid)
        => TryGetRandomBodyPartOfType(bodyUid, partType, out _, out partUid);

    /// <inheritdoc cref="TryGetRandomBodyPartOfType(EntityUid, BodyPartType, out Entity{OrganComponent}?)"/>
    public bool TryGetRandomBodyPartOfType(Entity<BodyComponent> bodyEntity, BodyPartType partType, out System.Random? predictedRandom, [NotNullWhen(true)] out EntityUid? partUid)
    {
        var eligible = new ValueList<EntityUid>();
        foreach (var possiblePartUid in bodyEntity.Comp.RecursiveChildUids)
        {
            if (!TryComp<OrganComponent>(possiblePartUid, out var organComponent))
                continue;

            var otherPartType = GetPartType(organComponent.Category);
            if (otherPartType == BodyPartType.Other || // because its 0 so hasflag would always return true
                !partType.HasFlag(otherPartType))
                continue;

            eligible.Add(possiblePartUid);
        }

        if (eligible.Count == 0)
        {
            partUid = null;
            predictedRandom = null;
            return false;
        }
        else if (eligible.Count == 1)
        {
            partUid = eligible.First();
            predictedRandom = null;
            return true;
        }

        predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(bodyEntity.Owner, EntityManager),
            eligible.Count
        );

        partUid = eligible[predictedRandom.Next(eligible.Count)];
        return true;
    }
}

[Flags]
[Serializable, NetSerializable]
public enum BodyPartType
{
    Other = 0,
    Torso = 1 << 0,
    Head = 1 << 1,
    Arm = 1 << 2,
    Hand = 1 << 3,
    Leg = 1 << 4,
    Foot = 1 << 5,
    Tail = 1 << 6
}
