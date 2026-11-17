using Content.Client._KS14.Graphics;
using Content.Shared._KS14.FieldCommander;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.FieldCommander;

/// <summary>
///     A quick flash of coloured static over a field commander defender's sprite as they go critical, and again, in
///         another colour, as they die: a post-shader that fades away over a fraction of a second.
/// </summary>
public sealed partial class KsFieldCommanderDownFlashSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> FlashShader = "KsDownFlash";

    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<KsFieldCommanderDefenderComponent> _defenderQuery = default!;
    [Dependency] private EntityQuery<KsFieldCommanderDownFlashVisualsComponent> _visualsQuery = default!;

    /// <summary>
    ///     Starts keeping an eye on a defender, as they are first seen: whatever state they are already in is not news.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsFieldCommanderDefenderComponent> entity, ref ComponentStartup args)
    {
        if (_visualsQuery.HasComp(entity))
            return;

        var visualsComponent = AddComp<KsFieldCommanderDownFlashVisualsComponent>(entity);
        visualsComponent.LastState = _mobStateQuery.TryComp(entity, out var mobStateComponent)
            ? mobStateComponent.CurrentState
            : MobState.Alive;
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsFieldCommanderDefenderComponent> entity, ref ComponentShutdown args)
    {
        if (_visualsQuery.TryComp(entity, out var visualsComponent))
        {
            EndFlash((entity.Owner, visualsComponent));
            RemCompDeferred<KsFieldCommanderDownFlashVisualsComponent>(entity.Owner);
        }
    }

    /// <summary>
    ///     Notes a defender's state, and flashes them if it is a change to critical or dead.
    /// </summary>
    private void Observe(Entity<KsFieldCommanderDownFlashVisualsComponent> entity, MobState state)
    {
        // Every frame for every defender, and almost always no change: that is checked before anything is looked up.
        if (entity.Comp.LastState == state)
            return;

        entity.Comp.LastState = state;

        if (_defenderQuery.TryComp(entity, out var defenderComponent) &&
            defenderComponent.Flashes.TryGetValue(state, out var settings))
            StartFlash(entity, settings);
        else
            EndFlash(entity); // revived
    }

    private void StartFlash(Entity<KsFieldCommanderDownFlashVisualsComponent> entity, KsFieldCommanderDownFlashSettings settings)
    {
        if (!_spriteQuery.TryComp(entity, out var spriteComponent))
            return;

        // A new flash replaces one still in progress.
        EndFlash(entity);

        var shader = ProtoMan.Index(FlashShader).InstanceUnique();
        entity.Comp.Shader = shader;
        entity.Comp.StartTime = _gameTiming.CurTime + _gameTiming.TickRemainder;
        entity.Comp.Duration = settings.Duration;
        entity.Comp.Progress = 0f;
        entity.Comp.Color = settings.Color;
        entity.Comp.Seed = entity.Owner.Id % 97;

        ApplyParameters(entity.Comp);

        // Stacks with any post-shader the sprite already has, such as a dissolve: ordered before the outlines.
        _spriteSystem.SetPostShader((entity.Owner, spriteComponent), new SpriteComponent.PostShaderArgs(KsPostShaderIds.DownFlash, shader)
        {
            Before = KsPostShaderIds.BeforeOutlines,
        });
    }

    private void EndFlash(Entity<KsFieldCommanderDownFlashVisualsComponent> entity)
    {
        if (entity.Comp.Shader == null)
            return;

        if (!TerminatingOrDeleted(entity) && _spriteQuery.TryComp(entity, out var spriteComponent))
            _spriteSystem.RemovePostShader((entity.Owner, spriteComponent), KsPostShaderIds.DownFlash);

        entity.Comp.Shader.Dispose();
        entity.Comp.Shader = null;
    }

    /// <summary>
    ///     Every uniform, every frame, so that it can never draw with another instance's.
    /// </summary>
    private static void ApplyParameters(KsFieldCommanderDownFlashVisualsComponent visualsComponent)
    {
        visualsComponent.Shader?.SetParameter("progress", visualsComponent.Progress);
        visualsComponent.Shader?.SetParameter("seed", visualsComponent.Seed);
        visualsComponent.Shader?.SetParameter("flashColor", visualsComponent.Color);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime + _gameTiming.TickRemainder;

        var enumerator = EntityQueryEnumerator<KsFieldCommanderDownFlashVisualsComponent, MobStateComponent>();
        while (enumerator.MoveNext(out var uid, out var visualsComponent, out var mobStateComponent))
        {
            // One taken off this frame is shut down, but still stored: a flash started on it would never be ended.
            if (visualsComponent.LifeStage > ComponentLifeStage.Running)
                continue;

            // The mob state is checked here for want of anything to hear it change: MobStateComponent never raises
            //     AfterAutoHandleStateEvent, and MobStateChangedEvent only comes on the client for what it predicts itself.
            Observe((uid, visualsComponent), mobStateComponent.CurrentState);

            if (visualsComponent.Shader == null)
                continue;

            var progress = visualsComponent.Duration > TimeSpan.Zero
                ? (float)Math.Clamp((curTime - visualsComponent.StartTime) / visualsComponent.Duration, 0d, 1d)
                : 1f;
            visualsComponent.Progress = progress;

            if (progress >= 1f)
            {
                EndFlash((uid, visualsComponent));
                continue;
            }

            ApplyParameters(visualsComponent);
        }
    }
}
