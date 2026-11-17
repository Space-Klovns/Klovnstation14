using Content.Client.UserInterface.Controls;
using Content.Shared.ActionBlocker;
using Content.Shared.Ghost;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     tg's <c>Window__dimmer</c> (layouts/Window.tsx: <c>config.status &lt; UI_INTERACTIVE</c>): a window that can be seen but
///         not used is washed over. In SS14 the state is real. Nothing closes a bound user interface when its user is stunned,
///         asleep, cuffed or in crit; the window stays open and the server cancels every message but a close
///         (<c>SharedInteractionSystem.OnBoundInterfaceInteractAttempt</c>). This runs the same checks on the client, for the
///         windows of bound user interfaces that have the tgui chrome, and never for a ghost.
/// </summary>
public sealed partial class KsTguiWindowDimmerUIController : UIController
{
    /// <summary>
    ///     Seconds between checks.
    /// </summary>
    private const float PollInterval = 0.25f;

    [Dependency] private IPlayerManager _playerManager = default!;

    // Polled because there is nothing to hang on: whether someone can interact is the answer to events (InteractionAttemptEvent,
    //      GettingInteractedWithAttemptEvent) that any system may cancel for any reason, with no event for the answer changing.
    //      A handful of open windows at four checks a second costs nothing.
    private readonly List<Tracked> _tracked = new();
    private float _sincePoll;

    private sealed record Tracked(BoundUserInterface Bui, BaseWindow Window, KsTguiWindowDimmer Dimmer);

    /// <summary>
    ///     Starts watching a bound user interface's window. Called for every window a bound user interface makes (see
    ///         <c>KsBoundUserInterfaceExt.WithGuide</c>). The window disposes with its interface, which is what ends the watch.
    /// </summary>
    public void Track(BoundUserInterface bui, BaseWindow window)
    {
        // Only the windows the tgui chrome dresses (KsTguiChromeUIController.SyncWindow). A bare BaseWindow, such as a
        //      RadialMenu, may treat every child as its own: the radial menu takes each one for a menu layer.
        if (window is not (DefaultWindow or FancyWindow))
            return;

        foreach (var tracked in _tracked)
        {
            if (tracked.Window == window)
                return;
        }

        var dimmer = new KsTguiWindowDimmer { Visible = false };
        window.AddChild(dimmer);
        var newlyTracked = new Tracked(bui, window, dimmer);
        _tracked.Add(newlyTracked);

        // The first state is in the title bar from the start, as tg's status is, and not eased to after the first poll.
        UpdateTracked(newlyTracked);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _sincePoll += args.DeltaSeconds;
        if (_sincePoll < PollInterval)
            return;

        _sincePoll = 0f;
        Refresh();
    }

    /// <summary>
    ///     Checks every watched window now. Also what the poll runs.
    /// </summary>
    public void Refresh()
    {
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            var tracked = _tracked[i];
            if (tracked.Window.Disposed)
            {
                _tracked.RemoveAt(i);
                continue;
            }

            UpdateTracked(tracked);
        }
    }

    private void UpdateTracked(Tracked tracked)
    {
        var status = GetStatus(tracked.Bui);

        // The title bar's icon is the same answer, drawn the same way tg does (layouts/TitleBar.tsx), so no second poll.
        UIManager.GetUIController<KsTguiChromeUIController>().SetStatus(tracked.Window, status);

        tracked.Dimmer.Visible = tracked.Window.IsInsideTree
            && KsTguiChromeUIController.HasChrome(tracked.Window)
            && status == KsTguiWindowStatus.Disabled;

        if (!tracked.Dimmer.Visible)
        {
            tracked.Dimmer.ReleaseCapture();
            return;
        }

        // What it captures is what is drawn before it, so it goes after everything the window has, whatever was added later.
        if (tracked.Dimmer.GetPositionInParent() != tracked.Window.ChildCount - 1)
            tracked.Dimmer.SetPositionLast();
    }

    /// <summary>
    ///     Whether the local player has this interface open but would have its messages refused by the server.
    /// </summary>
    public bool IsDimmed(BoundUserInterface bui)
    {
        return GetStatus(bui) == KsTguiWindowStatus.Disabled;
    }

    /// <summary>
    ///     What the server would let the local player do with this interface, as tgui's three states.
    /// </summary>
    /// <remarks>
    ///     <see cref="KsTguiWindowStatus.Update"/> is a ghost looking at an interface it may not use. The server lets a ghost open
    ///         any interface that does not <c>BlockSpectators</c>, even though <c>CanInteract</c> fails for it, and then refuses every
    ///         other message (<c>SharedInteractionSystem.OnBoundInterfaceInteractAttempt</c>). That is tg's <c>UI_UPDATE</c>: the window
    ///         stays up to date for an observer who cannot touch it. A ghost that can interact (an admin ghost) is interactive.
    ///         tg dims an observer only when fully disabled, which has no counterpart here: a ghost is never washed over.
    ///         An interface with <c>BlockSpectators</c> is never opened for a ghost, so no window reaches here.
    /// </remarks>
    public KsTguiWindowStatus GetStatus(BoundUserInterface bui)
    {
        if (_playerManager.LocalEntity is not { } user)
            return KsTguiWindowStatus.Interactive;

        var owner = bui.Owner;
        if (!EntityManager.EntityExists(owner))
            return KsTguiWindowStatus.Interactive;

        // An interface with input validation off accepts anything, so nothing to show.
        if (EntityManager.System<SharedUserInterfaceSystem>().TryGetInterfaceData(owner, bui.UiKey, out var data)
            && !data.RequireInputValidation)
        {
            return KsTguiWindowStatus.Interactive;
        }

        var blocker = EntityManager.System<ActionBlockerSystem>();
        var canUse = blocker.CanInteract(user, owner)
            && !(EntityManager.TryGetComponent(owner, out ActivatableUIComponent? activatable)
                && activatable.RequiresComplex
                && !blocker.CanComplexInteract(user));

        if (canUse)
            return KsTguiWindowStatus.Interactive;

        return EntityManager.HasComponent<GhostComponent>(user) ? KsTguiWindowStatus.Update : KsTguiWindowStatus.Disabled;
    }
}
