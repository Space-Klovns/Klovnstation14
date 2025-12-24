// SPDX-FileCopyrightText: 2023 Chief-Engineer
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2023 Kara
// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2023 ShadowCommander
// SPDX-FileCopyrightText: 2023 metalgearsloth
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Destructible; // KS14 Addition
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components; // KS14 Addition
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Teleportation.Components; // KS14 Addition
using Content.Shared.Teleportation.Systems;
using Robust.Shared.Map;

namespace Content.Server.Teleportation;

public sealed class PortalSystem : SharedPortalSystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly DestructibleSystem _destructibleSystem = default!; // KS14 Addition

    // TODO Move to shared
    protected override void LogTeleport(EntityUid portal, EntityUid subject, EntityCoordinates source,
        EntityCoordinates target)
    {
        if (HasComp<MindContainerComponent>(subject) && !HasComp<GhostComponent>(subject))
            _adminLogger.Add(LogType.Teleport, LogImpact.Low, $"{ToPrettyString(subject):player} teleported via {ToPrettyString(portal)} from {source} to {target}");
    }

    // KS14 Implementation
    public override bool OnTelefrag(Entity<BodyComponent?> hitEntity, in Entity<PortalComponent> portalEntity)
    {
        if (!base.OnTelefrag(hitEntity, portalEntity))
            return false;

        // whatever lmao
        _destructibleSystem.DestroyEntity(hitEntity.Owner);
        return true;
    }
}
