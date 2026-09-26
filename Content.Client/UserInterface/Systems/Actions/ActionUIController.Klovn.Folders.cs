// KS14: added in this fork
using System.Collections.Generic;
using System.Linq;
using Content.Client._KS14.Actions;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Shared.Actions.Components;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Client-only action folders: drop one action onto another to group them, click the group to step
///         into it, and click the back entry to step out again.
/// </summary>
/// <remarks>
///     A folder is a nullspace entity carrying an <see cref="ActionComponent"/> and a
///         <see cref="KsActionFolderComponent"/>, so it can sit in <c>_actions</c> anywhere a real action
///         can. None of it is known to the server.
///     Folders are one level deep. While one is open, <c>_actions</c> holds the folder's contents and the
///         root bar is parked in <see cref="_rootFolderActions"/>.
/// </remarks>
public sealed partial class ActionUIController
{
    /// <summary>
    ///     Index in <c>_actions</c> that the back entry occupies while a folder is open.
    /// </summary>
    private const int ExitFolderActionIndex = 0;

    private static readonly SpriteSpecifier FolderExitIcon = new SpriteSpecifier.Texture(
        new ResPath("/Textures/Interface/Default/left_arrow.svg.192dpi.png"));

    private EntityUid? _openActionFolder;
    private EntityUid? _exitFolderAction;
    private List<EntityUid?>? _rootFolderActions;

    /// <summary>
    ///     Handles a press on a folder or on the back entry, if that is what was pressed.
    /// </summary>
    /// <returns>True if the press was a folder navigation and should not be treated as an action use.</returns>
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

    /// <summary>
    ///     Swaps the bar out for the folder's contents, with a back entry in the first slot.
    /// </summary>
    private void EnterActionFolder(EntityUid folderUid, KsActionFolderComponent folderComponent)
    {
        if (_actionsSystem == null)
            return;

        // Not a collection expression: a spread into a List<T> lowers to CollectionsMarshal.SetCount,
        // which the content sandbox rejects.
        _rootFolderActions = new List<EntityUid?>(_actions);
        _openActionFolder = folderUid;
        _exitFolderAction = CreateFolderAction(isExit: true, folderUid);

        _actions.Clear();
        _actions.Add(_exitFolderAction);
        _actions.AddRange(folderComponent.Actions.Cast<EntityUid?>());
        RefreshActionBar();
    }

    /// <summary>
    ///     Puts the root bar back and throws the back entry away.
    /// </summary>
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

    /// <summary>
    ///     Spawns the nullspace entity that stands in for a folder, or for the back entry.
    /// </summary>
    /// <param name="isExit">Whether this is the back entry rather than a folder.</param>
    /// <param name="iconSourceUid">Action whose icon the folder borrows. Ignored for the back entry.</param>
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

    /// <summary>
    ///     Whether a drop onto this button has to be refused because it is the back entry's slot.
    /// </summary>
    /// <remarks>
    ///     Without this, a member dragged onto slot zero displaces the back entry into the folder's own
    ///         member list, where <see cref="ExitActionFolder"/> then deletes it - leaving a dangling uid
    ///         in the folder that both the bar and the save path go on to read.
    /// </remarks>
    private bool IsExitFolderSlot(ActionButton button)
    {
        return _openActionFolder != null &&
               _container is { } container &&
               container.TryGetButtonIndex(button, out var buttonIndex) &&
               buttonIndex == ExitFolderActionIndex;
    }

    /// <summary>
    ///     Shared guards for both drop-to-group paths.
    /// </summary>
    private bool CanFoldActions(ActionButton draggedButton, ActionButton targetButton, out EntityUid draggedActionUid)
    {
        draggedActionUid = default;

        if (!_actionFoldersEnabled ||
            _openActionFolder != null ||
            draggedButton.Action is not { } draggedAction ||
            targetButton.Action == null ||
            EntityManager.HasComponent<KsActionFolderComponent>(draggedAction))
        {
            return false;
        }

        draggedActionUid = draggedAction.Owner;
        return true;
    }

    /// <summary>
    ///     Groups two loose actions into a new folder, which takes the target's slot.
    /// </summary>
    private bool TryCreateActionFolder(ActionButton draggedButton, ActionButton targetButton)
    {
        if (!CanFoldActions(draggedButton, targetButton, out var draggedActionUid) ||
            targetButton.Action is not { } targetAction ||
            draggedActionUid == targetAction.Owner ||
            EntityManager.HasComponent<KsActionFolderComponent>(targetAction) ||
            _container is not { } container ||
            !container.TryGetButtonIndex(targetButton, out var targetIndex))
        {
            return false;
        }

        var folderUid = CreateFolderAction(isExit: false, targetAction);
        var folderComponent = EntityManager.GetComponent<KsActionFolderComponent>(folderUid);
        folderComponent.Actions.Add(targetAction);
        folderComponent.Actions.Add(draggedActionUid);
        _actions[targetIndex] = folderUid;

        // Blank the slot the dragged action came from rather than removing it. _actions is indexed by
        // hotbar slot, so removing would shift every later action one key to the left.
        if (draggedButton.Parent is ActionButtonContainer &&
            container.TryGetButtonIndex(draggedButton, out var draggedIndex))
        {
            _actions[draggedIndex] = null;
        }

        RefreshActionBar();
        SaveActionConfigurationAfterChange();
        return true;
    }

    /// <summary>
    ///     Drops a loose action into an existing folder.
    /// </summary>
    private bool TryAddActionToFolder(ActionButton draggedButton, ActionButton targetButton)
    {
        if (!CanFoldActions(draggedButton, targetButton, out var draggedActionUid) ||
            targetButton.Action is not { } targetAction ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(targetAction, out var targetFolderComponent) ||
            targetFolderComponent.IsExit)
        {
            return false;
        }

        var rootActions = _rootFolderActions ?? _actions;

        // Blank rather than remove, so the rest of the bar keeps its slots.
        for (var slotIndex = 0; slotIndex < rootActions.Count; slotIndex++)
        {
            if (rootActions[slotIndex] == draggedActionUid)
                rootActions[slotIndex] = null;
        }

        foreach (var rootActionUid in rootActions)
        {
            if (rootActionUid is not { } folderUid || folderUid == targetAction.Owner ||
                !EntityManager.TryGetComponent<KsActionFolderComponent>(folderUid, out var folderComponent))
            {
                continue;
            }

            folderComponent.Actions.RemoveAll(actionUid => actionUid == draggedActionUid);
        }

        if (!targetFolderComponent.Actions.Contains(draggedActionUid))
            targetFolderComponent.Actions.Add(draggedActionUid);

        RefreshActionBar();
        SaveActionConfigurationAfterChange();
        return true;
    }

    /// <summary>
    ///     Right-clicking a folder dissolves it, putting its members back on the root bar.
    /// </summary>
    /// <remarks>
    ///     The members have to come back. A saved layout treats an action that is known but unplaced as
    ///         deliberately removed, so dropping them here would strand them off the bar for good, with
    ///         no UI to get them back.
    /// </remarks>
    private bool TryHandleFolderRightClick(ActionButton button)
    {
        if (button.Action is not { } action ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(action, out var folderComponent))
        {
            return false;
        }

        if (_openActionFolder != null || folderComponent.IsExit)
            return true;

        var releasedActionUids = folderComponent.Actions.ToArray();
        SetAction(button, null);
        EntityManager.DeleteEntity(action);

        foreach (var releasedActionUid in releasedActionUids)
        {
            AddActionToRoot(releasedActionUid);
        }

        RefreshActionBar();
        SaveActionConfigurationAfterChange();
        return true;
    }

    /// <summary>
    ///     Whether this button holds a folder or the back entry, neither of which can be dragged.
    /// </summary>
    private bool IsFolderAction(ActionButton button)
    {
        return button.Action is { } action && EntityManager.HasComponent<KsActionFolderComponent>(action);
    }

    /// <summary>
    ///     Writes edits made inside an open folder back onto the folder itself.
    /// </summary>
    private void SyncOpenActionFolder()
    {
        if (_openActionFolder is not { } folderUid ||
            !EntityManager.TryGetComponent<KsActionFolderComponent>(folderUid, out var folderComponent))
        {
            return;
        }

        folderComponent.Actions.Clear();
        foreach (var actionUid in _actions.Skip(ExitFolderActionIndex + 1))
        {
            if (actionUid is { } uid)
                folderComponent.Actions.Add(uid);
        }
    }

    /// <summary>
    ///     Pushes <c>_actions</c> to the bar, folder entries included.
    /// </summary>
    private void RefreshActionBar()
    {
        if (_actionsSystem != null)
            _container?.SetActionData(_actionsSystem, _actions.ToArray());
    }

    /// <summary>
    ///     Whether this action already sits somewhere on the bar, at root or inside a folder.
    /// </summary>
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

    /// <summary>
    ///     Places a newly granted action on the root bar, if it is not placed already.
    /// </summary>
    private void AddActionToRoot(EntityUid actionUid)
    {
        if (ContainsAssignedAction(actionUid))
            return;

        (_rootFolderActions ?? _actions).Add(actionUid);
    }

    /// <summary>
    ///     Takes a revoked action off the bar and out of every folder.
    /// </summary>
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

    /// <summary>
    ///     Dissolves every folder, spilling their members onto the bar in place. Used when folders are
    ///         switched off.
    /// </summary>
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

    /// <summary>
    ///     Throws every folder entity away without spilling its members. Used when the action set the
    ///         folders belonged to is going away anyway.
    /// </summary>
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
