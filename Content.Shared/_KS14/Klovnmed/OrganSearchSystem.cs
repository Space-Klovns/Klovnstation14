// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body;
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

    // /// <summary>
    // ///     Populates a ValueList with bodyparts of a given type
    // ///         by searching recursively.
    // ///
    // ///     Supports <see cref="partType"/> having more than one bit set.
    // /// </summary>
    // public void SearchRecursiveForTypeAndPopulate(EntityUid uid, BodyPartType partType, ref ValueList<Entity<OrganComponent>> list)
    // {
    //     if (_organQuery.TryGetComponent(uid, out var bodyPartComponent) &&
    //         partType.HasFlag(GetPartType(bodyPartComponent.Category))) // check if the parttype fits
    //     {
    //         list.Add((uid, bodyPartComponent));
    //         return;
    //     }

    //     if (!_containerManagerQuery.TryGetComponent(uid, out var containerManagerComponent))
    //         return;

    //     foreach (var container in _containerSystem.GetAllContainers(uid, containerManager: containerManagerComponent))
    //     {
    //         foreach (var containedUid in container.ContainedEntities)
    //             SearchRecursiveForTypeAndPopulate(containedUid, partType, ref list);
    //     }
    // }

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
        // var list = new ValueList<Entity<OrganComponent>>();
        // SearchRecursiveForTypeAndPopulate(bodyUid, partType, ref list);
        var list = bodyEntity.Comp.Organs?.ContainedEntities ?? [];

        if (list.Count == 0)
        {
            partUid = null;
            predictedRandom = null;
            return false;
        }
        else if (list.Count == 1)
        {
            partUid = list[0];
            predictedRandom = null;
            return true;
        }

        predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(bodyEntity.Owner, EntityManager),
            list.Count
        );

        partUid = list[predictedRandom.Next(list.Count)];
        return true;
    }
}

[Flags] // KS14 Klovnmed: added FlagsAttribute
[Serializable, NetSerializable]
public enum BodyPartType
{
    Other = 0,
    Torso = 1 << 0 /* KS14 change: added value */,
    Head = 1 << 1 /* KS14 change: added value */,
    Arm = 1 << 2 /* KS14 change: added value */,
    Hand = 1 << 3 /* KS14 change: added value */,
    Leg = 1 << 4 /* KS14 change: added value */,
    Foot = 1 << 5 /* KS14 change: added value */,
    Tail = 1 << 6 /* KS14 change: added value */
}
