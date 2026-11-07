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
        foreach (var tracked in _tracked)
        {
            if (tracked.Window == window)
                return;
        }

        var dimmer = new KsTguiWindowDimmer { Visible = false };
        window.AddChild(dimmer);
        _tracked.Add(new Tracked(bui, window, dimmer));
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

            tracked.Dimmer.Visible = tracked.Window.IsInsideTree
                && KsTguiChromeUIController.HasChrome(tracked.Window)
                && IsDimmed(tracked.Bui);
        }
    }

    /// <summary>
    ///     Whether the local player has this interface open but would have its messages refused by the server.
    /// </summary>
    public bool IsDimmed(BoundUserInterface bui)
    {
        if (_playerManager.LocalEntity is not { } user)
            return false;

        // A ghost may look at an interface it can't use; tg dims an observer only when the window is fully disabled, which has no
        //      counterpart here.
        if (EntityManager.HasComponent<GhostComponent>(user))
            return false;

        var owner = bui.Owner;
        if (!EntityManager.EntityExists(owner))
            return false;

        // An interface with input validation off accepts anything, so nothing to show.
        if (EntityManager.System<SharedUserInterfaceSystem>().TryGetInterfaceData(owner, bui.UiKey, out var data)
            && !data.RequireInputValidation)
        {
            return false;
        }

        var blocker = EntityManager.System<ActionBlockerSystem>();
        if (!blocker.CanInteract(user, owner))
            return true;

        return EntityManager.TryGetComponent(owner, out ActivatableUIComponent? activatable)
            && activatable.RequiresComplex
            && !blocker.CanComplexInteract(user);
    }
}
