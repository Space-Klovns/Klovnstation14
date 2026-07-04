using Content.Client._KS14.AdminMusic.UI;
using Content.Shared._KS14.AdminMusic;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.AdminMusic;

public sealed partial class KsAdminMusicManager
{
    public Control? ContainerControl = null;

    private void OnPopupCancelPressed(KsAdminMusicEntry entry)
    {
        // don't play it again
        _endedEntries.Add(entry);

        RemoveEntry(entry);
    }

    public void SetPopupContainer(Control containerControl)
    {
        ContainerControl = containerControl;
    }

    public bool TryClearPopupContainer()
    {
        if (ContainerControl is not { } ||
            ContainerControl.Disposed)
            return false;

        foreach (var data in _activeEntryData.Values)
        {
            if (data.Popup is not { } popup)
                continue;

            popup.Orphan();
            data.Popup = null;
        }

        return true;
    }

    public bool TryPopulatePopupContainer()
    {
        if (ContainerControl is not { } ||
            ContainerControl.Disposed)
            return false;

        foreach (var entry in _activeEntryData.Keys)
            AddToPopupContainer(entry);

        return true;
    }

    public bool AddToPopupContainer(KsAdminMusicEntry entry)
    {
        if (!_activeEntryData.TryGetValue(entry, out var entryData))
            return false;

        if (entryData.Popup is { })
        {
            _sawmill.Error($"Already had a popup that existed for entry! Sound path: {entry.SoundPath}");
            return false;
        }

        if (entryData.AudioSource is not { } audioSource ||
            !_resourceCache.TryGetResource<AudioResource>(entry.SoundPath.CanonPath, out var audioResource))
            return false;

        var popup = new KsAdminMusicPopup();
        popup.SetData(entry, audioResource.AudioStream, audioSource);
        popup.OnCancelPressed += OnPopupCancelPressed;

        ContainerControl!.AddChild(popup);

        entryData.Popup = popup;
        return true;
    }
}
