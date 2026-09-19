// KS14: added in this fork
using System.Collections.Generic;
using Content.Client._KS14.Actions;
using Content.Client._KS14.Actions.UI;
using Content.Client.Actions;
using Robust.Client.GameObjects;
using Content.Shared.Actions.Components;

namespace Content.Client.UserInterface.Systems.Actions.Controls;

/// <summary>
///     Draws the grid-of-previews icon that marks this button as holding an action folder.
/// </summary>
public sealed partial class ActionButton
{
    private KsActionFolderIcon? _ksFolderIcon;

    /// <summary>
    ///     Shows the folder icon if this button holds a folder, and hides it otherwise.
    /// </summary>
    /// <returns>True if a folder icon was drawn, meaning the ordinary action icon should be skipped.</returns>
    private bool TryUpdateKsFolderIcon(EntityUid actionUid)
    {
        if (!_entities.TryGetComponent<KsActionFolderComponent>(actionUid, out var folderComponent) ||
            folderComponent.IsExit)
        {
            HideKsFolderIcon();
            return false;
        }

        _ksFolderIcon ??= CreateKsFolderIcon();
        var actionsSystem = _entities.System<ActionsSystem>();
        var previews = new List<Entity<ActionComponent>>();
        foreach (var memberUid in folderComponent.Actions)
        {
            if (previews.Count >= KsActionFolderIcon.MaximumPreviews)
                break;

            if (actionsSystem.GetAction(memberUid) is { } memberAction)
                previews.Add(memberAction);
        }

        _spriteSys ??= _entities.System<SpriteSystem>();
        _ksFolderIcon.SetFolder(previews, _spriteSys);
        _ksFolderIcon.Visible = true;
        SetActionIcon(null);
        return true;
    }

    /// <summary>
    ///     Adds the folder icon to this button, directly beneath the highlight overlay.
    /// </summary>
    private KsActionFolderIcon CreateKsFolderIcon()
    {
        var icon = new KsActionFolderIcon(_entities)
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddChild(icon);

        // Sit where the big action icon does - under the highlight, the label and the cooldown - rather
        // than at a hardcoded index, which would silently mis-layer if the children are ever reordered.
        icon.SetPositionInParent(HighlightRect.GetPositionInParent());
        return icon;
    }

    /// <summary>
    ///     Hides the folder icon, if one was ever made for this button.
    /// </summary>
    private void HideKsFolderIcon()
    {
        if (_ksFolderIcon != null)
            _ksFolderIcon.Visible = false;
    }
}
