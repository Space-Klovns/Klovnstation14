// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Speczones;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Speczones;

/// <summary>
///     Must only be added as a component when <see cref="Prototype"/>
///         has been properly set to something.
/// </summary>
[RegisterComponent]
[UnsavedComponent]
public sealed partial class SpeczoneComponent : SharedSpeczoneComponent
{
    /// <summary>
    ///     ID of the <see cref="SpeczonePrototype"/> of this speczone.
    /// </summary>
    public ProtoId<SpeczonePrototype> PrototypeId = "default";

    /// <summary>
    ///     Entities with <see cref="SpeczoneEntryComponent"/> that are
    ///         assigned to this speczone.
    /// </summary>
    public HashSet<Entity<TransformComponent>> EntryMarkers = new();
}

/// <summary>
///     Marks an entity at which players can enter a speczone
///         by. Automatically added to a speczone's list of
///         entry-points when it is being loaded. Automatically removed
///         from it's corresponding speczone's list of entry-points
///         when the component is shutting down.
/// </summary>
[RegisterComponent, Access(typeof(SpeczoneSystem))]
public sealed partial class SpeczoneEntryComponent : Component;
