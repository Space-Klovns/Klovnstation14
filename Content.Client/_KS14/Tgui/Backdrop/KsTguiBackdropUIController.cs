using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Backdrop;

/// <summary>
///     What the backdrop blur shares per client: whether it is on, and whether a backdrop pass is running.
/// </summary>
/// <remarks>
///     The pass flag is read by the game viewport, which must not render the world again while the UI behind a popup is
///         redrawn. It lives here and not in a <c>static</c> because several clients share a process in tests.
/// </remarks>
public sealed partial class KsTguiBackdropUIController : UIController
{
    public const string BlurShader = "KsTguiBackdropBlur";
    public const string CompositeShader = "KsTguiBackdropComposite";
    public const string RasterShader = "KsTguiScaledRaster";

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IClyde _clyde = default!;

    public IUserInterfaceManager UiManager => UIManager;

    /// <summary>
    ///     Whether the UI behind a popup is being drawn into a backdrop right now.
    /// </summary>
    public bool PassActive { get; private set; }

    /// <summary>
    ///     Whether popups blur what is behind them: under the tgui theme, unless the player turned it off.
    /// </summary>
    public bool Enabled =>
        _configurationManager.GetCVar(KsCCVars.TguiStyle) && _configurationManager.GetCVar(KsCCVars.TguiBackdropBlur);

    /// <summary>
    ///     False inside a pass, so that a popup drawn while another is being blurred doesn't start a pass of its own.
    /// </summary>
    public bool CanBlur => Enabled && !PassActive;

    /// <summary>
    ///     Whether something may capture the UI behind it without blurring: under the tgui theme (the blur setting is only
    ///         about the blur), and not inside a pass.
    /// </summary>
    public bool CanCapture => _configurationManager.GetCVar(KsCCVars.TguiStyle) && !PassActive;

    /// <summary>
    ///     What an empty pixel of the backdrop is: what the UI is drawn over.
    /// </summary>
    public Color ClearColor
    {
        get
        {
            var root = UIManager.RootControl;
            var color = root.BackgroundColor
                ?? (root.TryGetStyleProperty(UIRoot.StylePropBackground, out Color styled) ? styled : Color.Black);
            return color.WithAlpha(1f);
        }
    }

    /// <summary>
    ///     Runs <paramref name="draw"/> with <see cref="PassActive"/> set, and clears it again however it ends.
    /// </summary>
    public void RunPass(Action draw)
    {
        if (PassActive)
            throw new InvalidOperationException("A backdrop pass is already running.");

        PassActive = true;
        try
        {
            draw();
        }
        finally
        {
            PassActive = false;
        }
    }

    public IRenderTexture CreateTarget(Vector2i size, string name)
    {
        // Filtered, because the blur samples between texels.
        return _clyde.CreateRenderTarget(
            size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name);
    }

    public ShaderInstance CreateShader(string id)
    {
        return _prototypeManager.Index<ShaderPrototype>(id).InstanceUnique();
    }
}
