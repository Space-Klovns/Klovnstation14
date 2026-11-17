using Content.Shared.CCVar;
using Content.Shared.Maps;

namespace Content.Server.Maps;

public sealed partial class GameMapManager
{
    /// <inheritdoc/>
    public event Action? KsSelectedMapChanged;

    private GameMapPrototype? _ksLastSelectedMap;

    private void KsInitialize()
    {
        // Registered after upstream's handler for the same cvar, so the selection has already been updated when this runs.
        _configurationManager.OnValueChanged(CCVars.GameMap, _ => KsNotifySelectedMapChanged());
        _ksLastSelectedMap = GetSelectedMap();
    }

    /// <summary>
    ///     Raises <see cref="KsSelectedMapChanged"/> if <see cref="GetSelectedMap"/> now returns a different map.
    ///     Called after every write to the selection.
    /// </summary>
    private void KsNotifySelectedMapChanged()
    {
        var selectedMap = GetSelectedMap();
        if (ReferenceEquals(selectedMap, _ksLastSelectedMap))
            return;

        _ksLastSelectedMap = selectedMap;
        KsSelectedMapChanged?.Invoke();
    }
}
