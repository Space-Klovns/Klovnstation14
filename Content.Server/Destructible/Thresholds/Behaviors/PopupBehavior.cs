using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Shared.Destructible;
using Content.Shared.Destructible.Thresholds.Behaviors;
using Content.Shared.Popups;

namespace Content.Server.Destructible.Thresholds.Behaviors;

/// <summary>
/// Shows a popup for everyone.
/// </summary>
[DataDefinition]
public sealed partial class PopupBehavior : IThresholdBehavior
{
    /// <summary>
    /// Locale id of the popup message.
    /// </summary>
    [DataField("popup", required: true)]
    public string Popup;

    /// <summary>
    /// Type of popup to show.
    /// </summary>
    [DataField("popupType")]
    public PopupType PopupType;

    /// <summary>
    /// Only the affected entity will see the popup.
    /// </summary>
    [DataField]
    public bool TargetOnly;

    public void Execute(EntityUid uid, SharedDestructibleSystem system, EntityUid? cause = null)
    {
        var popup = system.EntityManager.System<SharedPopupSystem>();
        // popup is placed at coords since the entity could be deleted after, no more popup then
        var coords = system.EntityManager.GetComponent<TransformComponent>(uid).Coordinates;

        if (TargetOnly)
            popup.PopupCoordinates(KsPopupMessage.Create /* KS14: localize popups on the recipient */(Popup), coords, uid, PopupType);
        else
            popup.PopupCoordinates(KsPopupMessage.Create /* KS14: localize popups on the recipient */(Popup), coords, PopupType);
    }
}
