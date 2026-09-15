// KS14: added in this fork
using System.Collections.Generic;
using System.Linq;
using Content.Client._KS14.Actions;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Shared.Actions.Components;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Actions;

public sealed partial class ActionUIController
{
    private static readonly SpriteSpecifier FolderExitIcon = new SpriteSpecifier.Texture(
        new ResPath("/Textures/Interface/Default/left_arrow.svg.192dpi.png"));

    private EntityUid? _openActionFolder;
    private EntityUid? _exitFolderAction;
    private List<EntityUid?>? _rootFolderActions;

    private bool TryActivateFolderAction(EntityUid actionUid)
    {
        if (!_actionFoldersEnabled ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(actionUid, out var folderComponent))
            return false;

        if (folderComponent.IsExit)
            ExitActionFolder();
        else if (_openActionFolder == null)
            EnterActionFolder(actionUid, folderComponent);

        return true;
    }

    private void EnterActionFolder(EntityUid folderUid, KsActionFolderComponent folderComponent)
    {
        if (_actionsSystem == null)
            return;

        _rootFolderActions = [.. _actions];
        _openActionFolder = folderUid;
        _exitFolderAction = CreateFolderAction(true, folderUid);

        _actions.Clear();
        _actions.Add(_exitFolderAction);
        _actions.AddRange(folderComponent.Actions.Cast<EntityUid?>());
        RefreshActionBar();
    }

    private void ExitActionFolder()
    {
        if (_rootFolderActions == null)
            return;

        _actions.Clear();
        _actions.AddRange(_rootFolderActions);
        _rootFolderActions = null;
        _openActionFolder = null;

        if (_exitFolderAction is { } exitUid)
            EntityManager.DeleteEntity(exitUid);

        _exitFolderAction = null;
        RefreshActionBar();
    }

    private EntityUid CreateFolderAction(bool isExit, EntityUid iconSourceUid)
    {
        var folderUid = EntityManager.SpawnEntity(null, MapCoordinates.Nullspace);
        var actionComponent = EntityManager.EnsureComponent<ActionComponent>(folderUid);
        var folderComponent = EntityManager.EnsureComponent<KsActionFolderComponent>(folderUid);
        folderComponent.IsExit = isExit;

        if (isExit)
            _actionsSystem?.SetIcon((folderUid, actionComponent), FolderExitIcon);
        else if (_actionsSystem?.GetAction(iconSourceUid) is { } sourceAction)
            _actionsSystem.SetIcon((folderUid, actionComponent), sourceAction.Comp.Icon);

        EntityManager.System<MetaDataSystem>().SetEntityName(folderUid,
            Loc.GetString(isExit ? "action-folder-exit-name" : "action-folder-name"));
        return folderUid;
    }

    private bool TryCreateActionFolder(ActionButton draggedButton, ActionButton targetButton)
    {
        if (!_actionFoldersEnabled ||
            _openActionFolder != null ||
            draggedButton.Action is not { } draggedAction ||
            targetButton.Action is not { } targetAction ||
            draggedAction.Owner == targetAction.Owner ||
            EntityManager.HasComponent<KsActionFolderComponent>(draggedAction) ||
            EntityManager.HasComponent<KsActionFolderComponent>(targetAction) ||
            _container?.TryGetButtonIndex(targetButton, out var targetIndex) != true)
        {
            return false;
        }

        var folderUid = CreateFolderAction(false, targetAction);
        var folderComponent = EntityManager.GetComponent<KsActionFolderComponent>(folderUid);
        folderComponent.Actions.Add(targetAction);
        folderComponent.Actions.Add(draggedAction);
        _actions[targetIndex] = folderUid;

        if (draggedButton.Parent is ActionButtonContainer &&
            _container.TryGetButtonIndex(draggedButton, out var draggedIndex))
        {
            _actions.RemoveAt(draggedIndex);
        }

        RefreshActionBar();
        SaveActionConfigurationAfterChange();
        return true;
    }

    private bool TryAddActionToFolder(ActionButton draggedButton, ActionButton targetButton)
    {
        if (!_actionFoldersEnabled ||
            _openActionFolder != null ||
            draggedButton.Action is not { } draggedAction ||
            targetButton.Action is not { } targetAction ||
            EntityManager.HasComponent<KsActionFolderComponent>(draggedAction) ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(targetAction, out var targetFolderComponent) ||
            targetFolderComponent.IsExit)
        {
            return false;
        }

        var rootActions = _rootFolderActions ?? _actions;
        rootActions.RemoveAll(actionUid => actionUid == draggedAction.Owner);
        foreach (var rootActionUid in rootActions)
        {
            if (rootActionUid is not { } folderUid || folderUid == targetAction.Owner ||
                !EntityManager.TryGetComponent<KsActionFolderComponent>(folderUid, out var folderComponent))
            {
                continue;
            }

            folderComponent.Actions.RemoveAll(actionUid => actionUid == draggedAction.Owner);
        }

        if (!targetFolderComponent.Actions.Contains(draggedAction.Owner))
            targetFolderComponent.Actions.Add(draggedAction.Owner);

        RefreshActionBar();
        SaveActionConfigurationAfterChange();
        return true;
    }

    private bool TryHandleFolderRightClick(ActionButton button)
    {
        if (button.Action is not { } action ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(action, out var folderComponent))
        {
            return false;
        }

        if (_openActionFolder == null && !folderComponent.IsExit)
        {
            SetAction(button, null);
            EntityManager.DeleteEntity(action);
        }

        return true;
    }

    private bool IsFolderAction(ActionButton button)
    {
        return button.Action is { } action && EntityManager.HasComponent<KsActionFolderComponent>(action);
    }

    private void SyncOpenActionFolder()
    {
        if (_openActionFolder is not { } folderUid ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(folderUid, out var folderComponent))
        {
            return;
        }

        folderComponent.Actions.Clear();
        foreach (var actionUid in _actions.Skip(1))
        {
            if (actionUid is { } uid)
                folderComponent.Actions.Add(uid);
        }
    }

    private void RefreshActionBar()
    {
        if (_actionsSystem != null)
            _container?.SetActionData(_actionsSystem, _actions.ToArray());
    }

    private bool ContainsAssignedAction(EntityUid actionUid)
    {
        var rootActions = _rootFolderActions ?? _actions;
        foreach (var rootActionUid in rootActions)
        {
            if (rootActionUid == actionUid)
                return true;

            if (rootActionUid is { } folderUid &&
                EntityManager.TryGetComponent<KsActionFolderComponent>(folderUid, out var folderComponent) &&
                folderComponent.Actions.Contains(actionUid))
            {
                return true;
            }
        }

        return false;
    }

    private void AddActionToRoot(EntityUid actionUid)
    {
        if (ContainsAssignedAction(actionUid))
            return;

        (_rootFolderActions ?? _actions).Add(actionUid);
    }

    private void RemoveActionFromFolders(EntityUid actionUid)
    {
        _actions.RemoveAll(uid => uid == actionUid);
        _rootFolderActions?.RemoveAll(uid => uid == actionUid);

        var rootActions = _rootFolderActions ?? _actions;
        foreach (var rootActionUid in rootActions)
        {
            if (rootActionUid is { } folderUid &&
                EntityManager.TryGetComponent<KsActionFolderComponent>(folderUid, out var folderComponent))
            {
                folderComponent.Actions.RemoveAll(uid => uid == actionUid);
            }
        }

        SyncOpenActionFolder();
    }

    private void FlattenActionFolders()
    {
        if (_openActionFolder != null)
            ExitActionFolder();

        var flattened = new List<EntityUid?>();
        foreach (var actionUid in _actions)
        {
            if (actionUid is { } uid &&
                EntityManager.TryGetComponent<KsActionFolderComponent>(uid, out var folder) &&
                !folder.IsExit)
            {
                flattened.AddRange(folder.Actions.Cast<EntityUid?>());
                EntityManager.DeleteEntity(uid);
                continue;
            }

            flattened.Add(actionUid);
        }

        _actions.Clear();
        _actions.AddRange(flattened);
        _rootFolderActions = null;
        _openActionFolder = null;
        _exitFolderAction = null;
    }
    private void ResetActionFolders()
    {
        if (_openActionFolder != null)
            ExitActionFolder();

        foreach (var actionUid in _actions.ToArray())
        {
            if (actionUid is { } uid && EntityManager.HasComponent<KsActionFolderComponent>(uid))
                EntityManager.DeleteEntity(uid);
        }

        _rootFolderActions = null;
        _openActionFolder = null;
        _exitFolderAction = null;
    }
}
