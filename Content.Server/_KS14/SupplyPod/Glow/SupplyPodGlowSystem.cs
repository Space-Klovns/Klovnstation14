using System.Numerics;
using Content.Shared._KS14.Sprite;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.SupplyPod.Glow;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Server._KS14.SupplyPod.Glow;

/// <summary>
///     tgstation's <c>add_glow()</c> / <c>end_glow()</c>: a glow rides the pod down, then stays
///         where the pod landed and burns out.
/// </summary>
public sealed partial class SupplyPodGlowSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private TransformSystem _transformSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SupplyPodGlowComponent, SupplyPodLaunchedEvent>(OnLaunched);
        SubscribeLocalEvent<SupplyPodGlowComponent, SupplyPodLandedEvent>(OnLanded);
        SubscribeLocalEvent<SupplyPodGlowComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnLaunched(Entity<SupplyPodGlowComponent> entity, ref SupplyPodLaunchedEvent args)
    {
        // A pod on the way up is not burning anything - the glow belongs to the descent.
        if (args.Ascending)
            return;

        if (entity.Comp.GlowEntity is not null)
            return;

        // Parenting to the pod is tgstation's vis_contents: the glow inherits the pod's transform,
        // so it rides the client-side descent animation for free.
        var glowUid = Spawn(entity.Comp.GlowProto, new EntityCoordinates(entity.Owner, Vector2.Zero));

        // The pod does the same while falling. Without it the glow can get yanked onto a grid
        // mid-descent and left behind.
        Transform(glowUid).GridTraversal = false;

        entity.Comp.GlowEntity = glowUid;
    }

    private void OnLanded(Entity<SupplyPodGlowComponent> entity, ref SupplyPodLandedEvent args)
    {
        ReleaseGlow(entity.Comp);
    }

    /// <summary>
    ///     A pod destroyed before it lands takes its glow with it, matching tgstation's
    ///         <c>qdel(glow_effect)</c> on destruction. Landing releases the glow first, so this
    ///         only ever catches the abnormal case.
    /// </summary>
    private void OnShutdown(Entity<SupplyPodGlowComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.GlowEntity is not { } glowUid)
            return;

        entity.Comp.GlowEntity = null;

        if (!TerminatingOrDeleted(glowUid))
            QueueDel(glowUid);
    }

    /// <summary>
    ///     Cuts the glow loose where the pod left it and starts it burning out. The pod is usually
    ///         deleted the moment it lands, so the glow has to stand on its own from here.
    /// </summary>
    private void ReleaseGlow(SupplyPodGlowComponent glowComponent)
    {
        if (glowComponent.GlowEntity is not { } glowUid)
            return;

        glowComponent.GlowEntity = null;

        if (TerminatingOrDeleted(glowUid))
            return;

        var glowTransformComponent = Transform(glowUid);
        var parentUid = glowTransformComponent.ParentUid;

        // Reparent off the pod, keeping the glow exactly where and how it currently sits. Passing
        // the pod's rotation along matters - the glow inherited it while parented, and without it
        // the glow snaps upright the instant it is cut loose.
        if (parentUid.IsValid() && !TerminatingOrDeleted(parentUid))
        {
            var podTransformComponent = Transform(parentUid);

            _transformSystem.SetCoordinates(
                glowUid,
                glowTransformComponent,
                podTransformComponent.Coordinates,
                rotation: podTransformComponent.LocalRotation
            );

            glowTransformComponent.GridTraversal = true;
        }

        var fadeComponent = EnsureComp<KsSpriteFadeOutComponent>(glowUid);
        fadeComponent.FadeStartTime = _gameTiming.CurTime;
        fadeComponent.FadeDuration = glowComponent.FadeDuration;
        Dirty(glowUid, fadeComponent);

        EnsureComp<TimedDespawnComponent>(glowUid).Lifetime =
            (float)(glowComponent.FadeDuration + glowComponent.LingerDuration).TotalSeconds;
    }
}
