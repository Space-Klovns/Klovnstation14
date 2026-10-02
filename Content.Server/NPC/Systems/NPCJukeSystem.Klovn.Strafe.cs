// KS14: added in this fork
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Events;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCJukeSystem
{
    /// <summary>
    ///     Steers a ranged NPC sideways across its line to the target, in steps of
    ///         <see cref="NPCJukeComponent.KsStrafeDuration"/> with pauses between, so two NPCs trading shots are not
    ///         both standing still. Each step picks a side at random, leaning towards the other one; a step that would
    ///         lose sight of the target is turned around. True if it steered.
    /// </summary>
    /// <param name="localTargetDirection">Unit direction to the target, in the steering's (grid-relative) frame.</param>
    private bool KsTryStrafe(NPCJukeComponent component,
        ref NPCSteeringEvent args,
        Vector2 localTargetDirection,
        MapCoordinates targetMapCoordinates)
    {
        if (component.KsStrafeDuration is not { } duration || duration <= 0f)
            return false;

        var now = _timing.CurTime;

        if (now >= component.KsStrafeEndsAt)
        {
            var pause = component.KsStrafePause * _random.NextFloat(0.5f, 1.5f);
            component.KsStrafeStartsAt = now + TimeSpan.FromSeconds(pause);
            component.KsStrafeEndsAt = component.KsStrafeStartsAt + TimeSpan.FromSeconds(duration * _random.NextFloat(0.6f, 1.4f));

            // Mostly back the other way, so it weaves rather than drifting off to one side.
            if (_random.Prob(0.7f))
                component.KsStrafeSide = -component.KsStrafeSide;

            return false;
        }

        if (now < component.KsStrafeStartsAt)
            return false;

        var across = new Vector2(-localTargetDirection.Y, localTargetDirection.X) * component.KsStrafeSide;

        // The steering frame is turned by OffsetRotation from the world; turn back to check sight in the world.
        var toWorld = -args.OffsetRotation;
        var steered = false;

        for (var i = 0; i < SharedNPCSteeringSystem.InterestDirections; i++)
        {
            var result = Vector2.Dot(across, NPCSteeringSystem.Directions[i]);
            if (result <= 0f)
                continue;

            var stepWorldPosition = args.WorldPosition + toWorld.RotateVec(NPCSteeringSystem.Directions[i]);
            if (!_interactionSystem.InRangeUnobstructed(new MapCoordinates(stepWorldPosition, targetMapCoordinates.MapId), targetMapCoordinates, range: -1f))
                continue;

            args.Steering.Interest[i] = MathF.Max(args.Steering.Interest[i], result);
            steered = true;
        }

        // Nowhere to go this way without losing the target: the next step goes the other way.
        if (!steered)
        {
            component.KsStrafeSide = -component.KsStrafeSide;
            component.KsStrafeEndsAt = now;
        }

        return steered;
    }
}
