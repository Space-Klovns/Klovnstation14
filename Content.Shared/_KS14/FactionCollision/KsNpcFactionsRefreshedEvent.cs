using Content.Shared.NPC.Components;

namespace Content.Shared._KS14.FactionCollision;

/// <summary>
///     Raised on an entity when its <see cref="NpcFactionMemberComponent"/> starts, or its factions have changed.
/// </summary>
[ByRefEvent]
public readonly record struct KsNpcFactionsRefreshedEvent;
