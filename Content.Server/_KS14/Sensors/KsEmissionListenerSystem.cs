using System.Numerics;
using Content.Shared._KS14.Sensors;
using Content.Shared._KS14.Sensors.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Sensors;

/// <summary>
///     Shared shape of the passive emission listeners (ELINT, RWR): sensors that
///         detect no grids by their own signature but read the active-emission
///         registry and file the EMITTING grid as a contact. The two differ only
///         in what they hear (ELINT: every cone it sits inside, sensitivity
///         scaled, self-blinded; RWR: only emissions illuminating its own grid,
///         never self-blinded); the detection they build and the signal-strength
///         measure they take must stay identical, or two listeners would disagree
///         about one emission. Abstract and never registered on its own, like
///         <see cref="KsLosSensorSystem"/> it extends.
/// </summary>
public abstract partial class KsEmissionListenerSystem : KsLosSensorSystem
{
    /// <summary>
    ///     Relative strength of a heard emission: how deep inside the emission's full
    ///         reach the listener sits (1 on top of the emitter, toward 0 at the
    ///         cone's edge). Floored just above 0 so a heard emission never reads as
    ///         nothing, and measured against the FULL reach rather than any
    ///         listener-specific scaled reach, so two different listeners agree
    ///         about the same emission.
    /// </summary>
    protected static float SignalStrength(Vector2 listener, Vector2 emitter, float reach)
    {
        if (reach <= 0f)
            return 1f;

        return Math.Clamp(1f - (listener - emitter).Length() / reach, 0.05f, 1f);
    }

    /// <summary>
    ///     A located-emitter contact: the emitter grid at its accurate centre of mass, an
    ///         anonymous blip (Obscured => no name, no silhouette), filed under the
    ///         listener's own type (radar heard, <paramref name="typeOverride"/> null) or a
    ///         Jammer override, carrying the heard emission's band/pattern/strength as
    ///         identification intel.
    /// </summary>
    protected KsSensorDetection BuildEmitterDetection(EntityUid emitterGrid, Dictionary<ProtoId<KsSensorIntelPrototype>, string>? intel, KsSensorType? typeOverride,
        ProtoId<KsEmitterBandPrototype>? band, KsEmissionPattern pattern, float signalStrength)
    {
        var bounds = new Box2();
        var center = Vector2.Zero;
        var isStatic = false;

        if (GridQuery.TryGetComponent(emitterGrid, out var grid))
            bounds = grid.LocalAABB;

        if (PhysicsQuery.TryGetComponent(emitterGrid, out var physics))
        {
            center = physics.LocalCenter;
            isStatic = physics.BodyType == BodyType.Static;
        }

        var (gridPos, gridRot) = XformSystem.GetWorldPositionRotation(emitterGrid);
        var com = gridPos + gridRot.RotateVec(center);

        return new KsSensorDetection(
            emitterGrid,
            com,
            gridRot,
            Vector2.Zero,
            isStatic,
            bounds,
            center,
            Name: null,
            Intel: intel,
            Obscured: true,
            TypeOverride: typeOverride,
            Band: band,
            Pattern: pattern,
            SignalStrength: signalStrength);
    }
}
