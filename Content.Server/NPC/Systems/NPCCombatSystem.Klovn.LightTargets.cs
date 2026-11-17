// KS14: added in this fork
using System.Numerics;
using Content.Server._KS14.NPC.Lights;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Lights as ranged targets. An operative that shoots out lights (<see cref="NpcLightShooterSystem"/>) hands
///         <c>GunOperator</c> a light, and what follows is every shot there is: <c>UpdateRanged</c>, dark aim, and
///         <c>NpcSquadFireLaneSystem.TryGetClearShot</c>. A light only differs in where its middle is and how much a shot at it
///         may be moved by: the round has to hit the fixture, which for a wall light is a narrow box against the wall, not the
///         entity's own position at the middle of the tile.
/// </summary>
public sealed partial class NPCCombatSystem
{
    [Dependency] private NpcLightShooterSystem _npcLightShooterSystem = default!;

    /// <summary>
    ///     Where a shot at <paramref name="targetUid"/> goes: its position, or for a light, the middle of its fixture.
    /// </summary>
    private Vector2 KsGetTargetPosition(EntityUid targetUid, Vector2 position)
    {
        return _npcLightShooterSystem.IsLight(targetUid) ? _npcLightShooterSystem.GetAimPoint(targetUid) : position;
    }

    /// <summary>
    ///     How far <see cref="Content.Server._KS14.NPC.Squad.NpcSquadFireLaneSystem.TryGetClearShot"/> may move a shot at
    ///         <paramref name="targetUid"/> and still hit it: a body's radius, or for a light the little of it there is.
    /// </summary>
    private float KsGetTargetRadius(EntityUid targetUid)
    {
        return _npcLightShooterSystem.IsLight(targetUid)
            ? NpcLightShooterSystem.AimTolerance
            : _npcSquadFireLaneSystem.GetBodyRadius(targetUid);
    }
}
