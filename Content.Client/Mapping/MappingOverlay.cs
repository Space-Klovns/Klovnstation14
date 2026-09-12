using Robust.Client.GameObjects; // KS14
// using Robust.Client.Input; // KS14: removed
// using Robust.Client.Player; // KS14: removed
// using Robust.Client.UserInterface; // KS14: removed
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using static Content.Client.Mapping.MappingState;

namespace Content.Client.Mapping;

public sealed partial class MappingOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded"; // KS14: mapping editor overhaul port
    [Dependency] private IEntityManager _entities = default!;
    /* [Dependency] private IPlayerManager _player = default!; */ // KS14: removed
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<EntityUid, Color> _oldColors = new();

    private readonly MappingState _state;
    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public MappingOverlay(MappingState state)
    {
        IoCManager.InjectDependencies(this);

        _state = state;
        _shader = _prototypes.Index(UnshadedShader).Instance(); // KS14: mapping editor overhaul port
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        foreach (var (id, color) in _oldColors)
        {
// KS14 start: mapping editor overhaul port
            if (_entities.TryGetComponent(id, out SpriteComponent? sprite))
                sprite.Color = color;
// KS14 end
        }

        _oldColors.Clear();

        var handle = args.WorldHandle;
        handle.UseShader(_shader);

        switch (_state.Meta.State) // KS14: mapping editor overhaul port
        {
            // KS14 start: mapping editor overhaul port
            case CursorState.Tile:
            {
                if (_state.GetHoveredTileBox2() is { } box)
                    args.WorldHandle.DrawRect(box, _state.Meta.Color);

                break;
            }
            // KS14 end
            case CursorState.Entity: // KS14: mapping editor overhaul port
            {
                if (_state.GetHoveredEntity() is { } entity &&
                    _entities.TryGetComponent(entity, out SpriteComponent? sprite))

                {
                    _oldColors[entity] = sprite.Color;
                    sprite.Color = _state.Meta.Color; // KS14: mapping editor overhaul port
                }

                break;
            }
            case CursorState.EntityOrTile:
            {
                if (_state.GetHoveredEntity() is { } entity &&
                    _entities.TryGetComponent(entity, out SpriteComponent? sprite))
                {
                    _oldColors[entity] = sprite.Color;
                    sprite.Color = _state.Meta.Color; // KS14: mapping editor overhaul port
                }
                // KS14 start: mapping editor overhaul port
                else if (_state.GetHoveredTileBox2() is { } box) // KS14: mapping editor overhaul port
                {
                    args.WorldHandle.DrawRect(box, _state.Meta.SecondColor ?? _state.Meta.Color); // KS14: mapping editor overhaul port
                }
                // KS14 end
                break;
            }
        }

        handle.UseShader(null);
    }
}
