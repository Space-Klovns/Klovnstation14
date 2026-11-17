// KS14: added in this fork
using System.Numerics;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Perception;

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
///     <para>
///         How dark the target looks comes from perception (<see cref="NpcPerceptionSystem.GetSeenLight"/>), which worked
///             it out when it last saw it, and is only asked when the offset is rolled: a tick in between costs a
///             component read and a turn.
///     </para>
/// </summary>
public sealed partial class NPCCombatSystem
{
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [Dependency] private EntityQuery<NpcDarkAimComponent> _darkAimQuery = default!;
    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;

    /// <summary>
    ///     How long an NPC that has lost its perception since it first shot goes before it is asked for again.
    /// </summary>
    private static readonly TimeSpan NoPerceptionRecheck = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Turns <paramref name="targetSpot"/> about <paramref name="worldPos"/> by however far off <paramref name="uid"/>'s aim
    ///         is going, for how dark <paramref name="targetUid"/> is to it.
    /// </summary>
    internal void KsApplyDarkAim(EntityUid uid, EntityUid targetUid, Vector2 worldPos, ref Vector2 targetSpot)
    {
        // A lit bulb is never hard to see, however dark it is round its fixture.
        if (_npcLightShooterSystem.IsLight(targetUid))
            return;

        if (!_darkAimQuery.TryComp(uid, out var darkAimComponent))
        {
            if (!_perceptionQuery.HasComp(uid))
                return;

            darkAimComponent = AddComp<NpcDarkAimComponent>(uid);
        }

        var now = _timing.CurTime;
        if (now >= darkAimComponent.RollAt || darkAimComponent.TargetUid != targetUid)
            RollDarkAim((uid, darkAimComponent), targetUid, now);

        if (darkAimComponent.Offset == Angle.Zero)
            return;

        targetSpot = worldPos + darkAimComponent.Offset.RotateVec(targetSpot - worldPos);
    }

    private void RollDarkAim(Entity<NpcDarkAimComponent> entity, EntityUid targetUid, TimeSpan now)
    {
        entity.Comp.TargetUid = targetUid;

        // Its perception was taken off since: aims true, and is looked at again in a while. Not TimeSpan.MaxValue: RollAt is
        //      moved on by however long the NPC is paused, which would overflow it.
        if (!_perceptionQuery.TryComp(entity.Owner, out var perceptionComponent))
        {
            entity.Comp.Offset = Angle.Zero;
            entity.Comp.RollAt = now + NoPerceptionRecheck;
            return;
        }

        entity.Comp.RollAt = now + perceptionComponent.DarkAimHold;
        entity.Comp.Offset = GetDarkAimSpread((entity.Owner, perceptionComponent), targetUid) is { } spread
            ? new Angle(spread.Theta * _random.NextFloat(-1f, 1f))
            : Angle.Zero;
    }

    /// <summary>
    ///     How far off <paramref name="observer"/>'s shots at <paramref name="targetUid"/> may go for the dark at most, or null
    ///         for not at all: lit well enough, close enough, seen in the dark, or no perception to see it with.
    /// </summary>
    public Angle? GetDarkAimSpread(Entity<NpcPerceptionComponent?> observer, EntityUid targetUid)
    {
        if (!_perceptionQuery.Resolve(observer.Owner, ref observer.Comp, false) || observer.Comp.DarkAimLightLevel <= 0f)
            return null;

        var seenLight = _npcPerceptionSystem.GetSeenLight(observer, targetUid);
        if (seenLight >= observer.Comp.DarkAimLightLevel)
            return null;

        var darkness = 1f - seenLight / observer.Comp.DarkAimLightLevel;
        return new Angle(observer.Comp.DarkAimSpread.Theta * darkness);
    }
}
