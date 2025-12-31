// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Damage;

namespace Content.Shared._KS14.Execution;

/// <summary>
/// Raised on an ammo entity (for guns that take physical ammo) or gun entity (for guns that don't take physical ammo, e.g. battery guns) 
/// when it is consumed by the GunExecutionSystem.
/// Systems that manage specific ammo types should subscribe to this and
/// populate the Damage field.
/// </summary>
/// <param name="TemporaryProjectileUid">Entity to be deleted by the system after the event is completed successfully..</param>
[ByRefEvent]
public record struct GunExecutedEvent(
    EntityUid User,
    EntityUid Target,
    DamageSpecifier? Damage = null)
{
    public bool Cancelled = false;
    public string? FailureReason = null;
}

/// <summary>
///     Raised on an ammo entity when it is successfully used in an execution.
/// </summary>
[ByRefEvent]
public record struct GunFinishedExecutionEvent();
