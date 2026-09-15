using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Actions.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Direction = Robust.Shared.Maths.Direction;

namespace Content.Client._KS14.Actions.UI;

/// <summary>
/// Draws a distinct folder frame containing layered previews of up to four actions.
/// </summary>
public sealed class KsActionFolderIcon : Control
{
    private const int MaximumPreviews = 4;
    private const float NativeEntityIconSize = 32f;

    private static readonly Color PreviewBackground = Color.FromHex("#151927E6");
    private static readonly Color[] BorderColors =
    [
        Color.FromHex("#56D8FFFF"),
        Color.FromHex("#E16BFFFF"),
        Color.FromHex("#FFD65CFF"),
        Color.FromHex("#65F2A0FF"),
    ];

    private readonly IEntityManager _entityManager;
    private readonly ActionPreview[] _previews = new ActionPreview[MaximumPreviews];

    public KsActionFolderIcon(IEntityManager entityManager)
    {
        _entityManager = entityManager;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;

        for (var index = 0; index < MaximumPreviews; index++)
            _previews[index] = new ActionPreview();
    }

    public void SetFolder(IEnumerable<Entity<ActionComponent>> actions, SpriteSystem spriteSystem)
    {
        var index = 0;
        foreach (var action in actions)
        {
            if (index >= MaximumPreviews)
                break;

            var icon = action.Comp.Toggled && action.Comp.IconOn is { } iconOn
                ? iconOn
                : action.Comp.Icon;
            var preview = _previews[index];
            preview.ActionTexture = icon != null ? spriteSystem.Frame0(icon) : null;
            preview.ActionColor = action.Comp.IconColor;
            preview.EntityIconUid = action.Comp.ItemIconStyle == ItemActionIconStyle.NoItem
                ? null
                : action.Comp.EntityIcon;
            preview.ItemIconStyle = action.Comp.ItemIconStyle;
            preview.Visible = true;
            index++;
        }

        for (; index < MaximumPreviews; index++)
            _previews[index].Visible = false;
    }

    protected override void Draw(DrawingHandleScreen screenHandle)
    {
        var bounds = (UIBox2) PixelSizeBox;
        screenHandle.DrawRect(bounds, PreviewBackground);

        for (var index = 0; index < MaximumPreviews; index++)
        {
            if (_previews[index].Visible)
                DrawActionPreview(screenHandle, _previews[index], GetPreviewBounds(bounds, index));
        }

        screenHandle.DrawLine(bounds.TopLeft, bounds.TopRight, BorderColors[0]);
        screenHandle.DrawLine(bounds.TopRight, bounds.BottomRight, BorderColors[1]);
        screenHandle.DrawLine(bounds.BottomRight, bounds.BottomLeft, BorderColors[2]);
        screenHandle.DrawLine(bounds.BottomLeft, bounds.TopLeft, BorderColors[3]);
    }

    private void DrawActionPreview(DrawingHandleScreen screenHandle, ActionPreview preview, UIBox2 bounds)
    {
        var smallSize = bounds.Size / 2f;
        var smallBounds = UIBox2.FromDimensions(bounds.BottomRight - smallSize, smallSize);

        switch (preview.ItemIconStyle)
        {
            case ItemActionIconStyle.BigItem:
                DrawEntityIcon(screenHandle, preview.EntityIconUid, bounds);
                DrawActionIcon(screenHandle, preview, smallBounds);
                break;
            case ItemActionIconStyle.BigAction:
                DrawActionIcon(screenHandle, preview, bounds);
                DrawEntityIcon(screenHandle, preview.EntityIconUid, smallBounds);
                break;
            case ItemActionIconStyle.NoItem:
                DrawActionIcon(screenHandle, preview, bounds);
                break;
        }
    }

    private void DrawEntityIcon(DrawingHandleScreen screenHandle, EntityUid? entityUid, UIBox2 bounds)
    {
        if (!_entityManager.TryGetComponent(entityUid, out SpriteComponent? spriteComponent))
            return;

        var spriteSystem = _entityManager.System<SpriteSystem>();
        spriteSystem.ForceUpdate(entityUid.Value);
        var scale = MathF.Min(bounds.Width, bounds.Height) / NativeEntityIconSize;
        screenHandle.DrawEntity(
            entityUid.Value,
            bounds.Center,
            new Vector2(scale),
            Angle.Zero,
            overrideDirection: Direction.South,
            sprite: spriteComponent);
    }

    private static void DrawActionIcon(DrawingHandleScreen screenHandle, ActionPreview preview, UIBox2 bounds)
    {
        if (preview.ActionTexture != null)
            screenHandle.DrawTextureRect(preview.ActionTexture, bounds, preview.ActionColor);
    }

    private UIBox2 GetPreviewBounds(UIBox2 bounds, int index)
    {
        var inset = 3f * UIScale;
        var gap = UIScale;
        var cellLength = (MathF.Min(bounds.Width, bounds.Height) - inset * 2f - gap) / 2f;
        var column = index % 2;
        var row = index / 2;
        var position = bounds.TopLeft + new Vector2(
            inset + column * (cellLength + gap),
            inset + row * (cellLength + gap));

        if (column == 0)
            position -= new Vector2(2f * UIScale);

        return UIBox2.FromDimensions(position, new Vector2(cellLength));
    }

    private sealed class ActionPreview
    {
        public Texture? ActionTexture;
        public Color ActionColor;
        public EntityUid? EntityIconUid;
        public ItemActionIconStyle ItemIconStyle;
        public bool Visible;
    }
}
