using System.Collections.Generic;
using Content.Client._KS14.Actions;
using Content.Client._KS14.Actions.UI;
using Content.Client.Actions;
using Content.Shared.Actions.Components;

namespace Content.Client.UserInterface.Systems.Actions.Controls;

public sealed partial class ActionButton
{
    private KsActionFolderIcon? _ksFolderIcon;

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
            if (previews.Count >= 4)
                break;

            if (actionsSystem.GetAction(memberUid) is { } memberAction)
                previews.Add(memberAction);
        }

        _ksFolderIcon.SetFolder(previews, _spriteSys!);
        _ksFolderIcon.Visible = true;
        SetActionIcon(null);
        return true;
    }

    private KsActionFolderIcon CreateKsFolderIcon()
    {
        var icon = new KsActionFolderIcon(_entities)
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddChild(icon);
        icon.SetPositionInParent(3);
        return icon;
    }

    private void HideKsFolderIcon()
    {
        if (_ksFolderIcon != null)
            _ksFolderIcon.Visible = false;
    }
}
