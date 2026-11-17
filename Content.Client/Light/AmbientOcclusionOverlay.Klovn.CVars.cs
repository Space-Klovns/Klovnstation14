using Content.Shared._KS14.CCVar;
using Content.Shared.CCVar;

namespace Content.Client.Light;

public sealed partial class AmbientOcclusionOverlay
{
    /// <summary>
    ///     The AO colour, cached from <see cref="CCVars.AmbientOcclusionColor"/>.
    /// </summary>
    public Color Color { get; private set; }

    private bool _drawEntities;

    // Held so they can be unsubscribed: the configuration manager outlives this overlay.
    private Action<string>? _colorHandler;
    private Action<bool>? _drawEntitiesHandler;

    private void SubscribeCVars()
    {
        _colorHandler = value => Color = Color.FromHex(value);
        _drawEntitiesHandler = value => _drawEntities = value;

        _cfgManager.OnValueChanged(CCVars.AmbientOcclusionColor, _colorHandler, invokeImmediately: true);
        _cfgManager.OnValueChanged(KsCCVars.AmbientOcclusionEntities, _drawEntitiesHandler, invokeImmediately: true);
    }

    private void UnsubscribeCVars()
    {
        if (_colorHandler != null)
        {
            _cfgManager.UnsubValueChanged(CCVars.AmbientOcclusionColor, _colorHandler);
            _colorHandler = null;
        }

        if (_drawEntitiesHandler != null)
        {
            _cfgManager.UnsubValueChanged(KsCCVars.AmbientOcclusionEntities, _drawEntitiesHandler);
            _drawEntitiesHandler = null;
        }
    }
}
