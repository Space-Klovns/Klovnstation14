using Content.Client.Graphics;
using Content.Shared._KS14.Perks.Support;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Perks.Support;

/// <summary>
///     Outlines a marked target (<see cref="KsMarkedTargetComponent"/>) for whoever may see the mark: the marker and its
///         side. A post shader of its own, so the hover outline still works on it.
/// </summary>
public sealed partial class KsMarkedTargetVisualsSystem : EntitySystem
{
    public const string PostShaderId = "ks-marked-target";

    private static readonly ProtoId<ShaderPrototype> OutlineShader = "SelectionOutline";

    /// <summary>
    ///     Outline thickness, in texture pixels.
    /// </summary>
    private const float OutlineWidth = 1.5f;

    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private KsMarkedTargetSystem _markedTargetSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    private readonly Dictionary<Color, ShaderInstance> _shaders = new();

    [SubscribeLocalEvent]
    private void OnStartup(Entity<KsMarkedTargetComponent> entity, ref ComponentStartup args)
    {
        UpdateOutline(entity);
    }

    [SubscribeLocalEvent]
    private void OnAfterHandleState(Entity<KsMarkedTargetComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        UpdateOutline(entity);
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsMarkedTargetComponent> entity, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(entity.Owner))
            _spriteSystem.RemovePostShader(entity.Owner, PostShaderId);
    }

    [SubscribeLocalEvent]
    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        var query = EntityQueryEnumerator<KsMarkedTargetComponent>();
        while (query.MoveNext(out var uid, out var markedComponent))
            UpdateOutline((uid, markedComponent));
    }

    private void UpdateOutline(Entity<KsMarkedTargetComponent> entity)
    {
        if (!TryComp<SpriteComponent>(entity.Owner, out var spriteComponent))
            return;

        if (_playerManager.LocalEntity is not { } viewerUid || !_markedTargetSystem.CanSeeMark(entity.Comp, viewerUid))
        {
            _spriteSystem.RemovePostShader((entity.Owner, spriteComponent), PostShaderId);
            return;
        }

        var shader = GetShader(entity.Comp.OutlineColor);
        if (_spriteSystem.TryGetPostShader((entity.Owner, spriteComponent), PostShaderId, out var entry) && entry.Shader == shader)
            return;

        _spriteSystem.SetPostShader((entity.Owner, spriteComponent), new SpriteComponent.PostShaderArgs(PostShaderId, shader)
        {
            After = ContentPostShaderIds.AfterBaseEffects,
        });
    }

    private ShaderInstance GetShader(Color color)
    {
        if (_shaders.TryGetValue(color, out var shader))
            return shader;

        shader = ProtoMan.Index(OutlineShader).InstanceUnique();
        shader.SetParameter("outline_width", OutlineWidth);
        shader.SetParameter("outline_color", color);
        shader.SetParameter("outline_fullbright", true);
        _shaders.Add(color, shader);
        return shader;
    }
}
