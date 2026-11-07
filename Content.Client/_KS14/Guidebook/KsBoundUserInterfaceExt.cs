using Content.Client._KS14.Tgui;
using Content.Client.Guidebook.Components;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using EngineExt = Robust.Client.UserInterface.BoundUserInterfaceExt;

// Not Content.Client._KS14.Guidebook, on purpose. The compiler looks for an extension method in the enclosing namespaces before
//      it looks at using directives, so these take the place of the engine's for every bound user interface in Content.Client.*,
//      with nothing to change at the call sites, and a new one is covered without knowing about this. In the fork's own
//      namespace nothing would call them.
namespace Content.Client;

/// <summary>
///     The engine's <c>CreateWindow</c> helpers, and then the header's guidebook button, for a window whose entity has a
///         <see cref="GuideHelpComponent"/>. This is the one place that has both the window and the entity it's for.
/// </summary>
public static class KsBoundUserInterfaceExt
{
    public static T CreateWindow<T>(this BoundUserInterface bui) where T : BaseWindow, new()
    {
        return bui.WithGuide(EngineExt.CreateWindow<T>(bui));
    }

    public static T CreateWindowCenteredLeft<T>(this BoundUserInterface bui) where T : BaseWindow, new()
    {
        return bui.WithGuide(EngineExt.CreateWindowCenteredLeft<T>(bui));
    }

    public static T CreateWindowCenteredRight<T>(this BoundUserInterface bui) where T : BaseWindow, new()
    {
        return bui.WithGuide(EngineExt.CreateWindowCenteredRight<T>(bui));
    }

    /// <summary>
    ///     For a window made some other way than the helpers above. Leaves one alone if it has chosen its own guides. A
    ///         <see cref="DefaultWindow"/> has nowhere to show them outside the tgui theme, and a bare <see cref="BaseWindow"/>
    ///         has no title bar under any theme.
    /// </summary>
    public static T WithGuide<T>(this BoundUserInterface bui, T window) where T : BaseWindow
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        if (!entityManager.TryGetComponent(bui.Owner, out GuideHelpComponent? guideHelpComponent))
            return window;

        if (window is FancyWindow { HelpGuidebookIds: null } fancyWindow)
            fancyWindow.HelpGuidebookIds = guideHelpComponent.Guides;
        else if (window is DefaultWindow defaultWindow)
            IoCManager.Resolve<IUserInterfaceManager>().GetUIController<KsTguiChromeUIController>().SetGuides(defaultWindow, guideHelpComponent.Guides);

        return window;
    }
}
