// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Content.Shared._KS14.Random.Helpers;
using Content.Shared.Body.Part;
using Robust.Shared.Collections;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.Klovnmed;

/// <summary>
///     All methods that search for bodyparts here assume that
///         all bodyparts will only either be contained in either a Body,
///         or another BodyPart.
/// </summary>
public sealed class BodyPartSearchSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    private EntityQuery<BodyPartComponent> _bodyPartQuery;
    private EntityQuery<ContainerManagerComponent> _containerManagerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _bodyPartQuery = GetEntityQuery<BodyPartComponent>();
        _containerManagerQuery = GetEntityQuery<ContainerManagerComponent>();
    }

    /// <summary>
    ///     Populates a ValueList with bodyparts of a given type
    ///         by searching recursively.
    ///
    ///     Supports <see cref="partType"/> having more than one bit set.
    /// </summary>
    public void SearchRecursiveForTypeAndPopulate(EntityUid uid, BodyPartType partType, ref ValueList<Entity<BodyPartComponent>> list)
    {
        if (_bodyPartQuery.TryGetComponent(uid, out var bodyPartComponent) &&
            partType.HasFlag(bodyPartComponent.PartType)) // check if the parttype fits
        {
            list.Add((uid, bodyPartComponent));
            return;
        }

        if (!_containerManagerQuery.TryGetComponent(uid, out var containerManagerComponent))
            return;

        foreach (var container in _containerSystem.GetAllContainers(uid, containerManager: containerManagerComponent))
        {
            foreach (var containedUid in container.ContainedEntities)
                SearchRecursiveForTypeAndPopulate(containedUid, partType, ref list);
        }
    }

    /// <summary>
    ///     Tries to get a random bodypart by searching recursively, returns
    ///         false if none was found.
    ///
    ///     Supports <see cref="partType"/> having more than one bit set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetRandomBodyPartOfType(EntityUid bodyUid, BodyPartType partType, [NotNullWhen(true)] out Entity<BodyPartComponent>? partEntity)
        => TryGetRandomBodyPartOfType(bodyUid, partType, out _, out partEntity);

    /// <inheritdoc cref="TryGetRandomBodyPartOfType(EntityUid, BodyPartType, out Entity{BodyPartComponent}?)"/>
    public bool TryGetRandomBodyPartOfType(EntityUid bodyUid, BodyPartType partType, out System.Random? predictedRandom, [NotNullWhen(true)] out Entity<BodyPartComponent>? partEntity)
    {
        var list = new ValueList<Entity<BodyPartComponent>>();
        SearchRecursiveForTypeAndPopulate(bodyUid, partType, ref list);

        if (list.Count == 0)
        {
            partEntity = null;
            predictedRandom = null;
            return false;
        }
        else if (list.Count == 1)
        {
            partEntity = list[0];
            predictedRandom = null;
            return true;
        }

        predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            (int)_gameTiming.CurTick.Value,
            KsSharedRandomExtensions.GetNetId(bodyUid, EntityManager),
            list.Count
        );

        partEntity = list[predictedRandom.Next(list.Count)];
        return true;
    }
}
