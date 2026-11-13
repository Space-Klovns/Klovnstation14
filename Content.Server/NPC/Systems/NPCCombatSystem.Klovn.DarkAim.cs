// KS14: added in this fork
using System.Numerics;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Systems;

namespace Content.Server.NPC.Systems;

/// <summary>
///     Shooting in the dark: an NPC that can see a hostile, but only just (<see cref="NpcPerceptionComponent.DarkAimLightLevel"/>),
///         aims off it, up to <see cref="NpcPerceptionComponent.DarkAimSpread"/> in pitch dark. The error wanders, held for
///         <see cref="NpcPerceptionComponent.DarkAimHold"/> at a time, rather than shaking every tick.
///     <para>
///         It moves the aim point, not the gun's spread, before the friendly fire checks: those see the shot as it will go,
///             so a squadmate is no more at risk in the dark than in the light. Only for NPCs with perception, and none for
///             one that sees in the dark, or at a hostile close enough to be seen anyway.
///     </para>
/// </summary>
public sealed partial class NPCCombatSystem
{
    [Dependency] private NpcLightDetectionSystem _npcLightDetectionSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [Dependency] private EntityQuery<NpcDarkAimComponent> _darkAimQuery = default!;
    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;

    /// <summary>
    ///     Turns <paramref name="targetSpot"/> about <paramref name="worldPos"/> by however far off <paramref name="uid"/>'s aim
    ///         is going, for how dark <paramref name="targetUid"/> is to it.
    /// </summary>
    internal void KsApplyDarkAim(EntityUid uid, EntityUid targetUid, Vector2 worldPos, ref Vector2 targetSpot)
    {
        if (GetDarkAimSpread(uid, targetUid) is not { } spread)
            return;

        var now = _timing.CurTime;
        if (!_darkAimQuery.TryComp(uid, out var darkAimComponent))
            darkAimComponent = AddComp<NpcDarkAimComponent>(uid);

        if (now >= darkAimComponent.RollAt)
        {
            darkAimComponent.Error = _random.NextFloat(-1f, 1f);
            darkAimComponent.RollAt = now + _perceptionQuery.Comp(uid).DarkAimHold;
        }

        var offset = new Angle(spread.Theta * darkAimComponent.Error);
        targetSpot = worldPos + offset.RotateVec(targetSpot - worldPos);
    }

    /// <summary>
    ///     How far off <paramref name="uid"/>'s shots at <paramref name="targetUid"/> may go for the dark at most, or null for
    ///         not at all: lit well enough, close enough, seen in the dark, or no perception to see it with.
    /// </summary>
    public Angle? GetDarkAimSpread(EntityUid uid, EntityUid targetUid)
    {
        if (!_perceptionQuery.TryComp(uid, out var perceptionComponent) || perceptionComponent.DarkAimLightLevel <= 0f)
            return null;

        var lightLevel = _npcLightDetectionSystem.GetPerceivedLightLevel(uid, targetUid, perceptionComponent.ProximityRange);
        var seenLight = _npcPerceptionSystem.GetSeenLightLevel((uid, perceptionComponent), lightLevel);

        if (seenLight >= perceptionComponent.DarkAimLightLevel)
            return null;

        var darkness = 1f - seenLight / perceptionComponent.DarkAimLightLevel;
        return new Angle(perceptionComponent.DarkAimSpread.Theta * darkness);
    }
}
