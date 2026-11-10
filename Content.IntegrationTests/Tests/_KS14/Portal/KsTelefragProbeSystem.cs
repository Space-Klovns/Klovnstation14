#nullable enable
using System.Collections.Generic;
using Content.Shared._KS14.PopupLocalization;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Portal;

/// <summary>
///     Counts what a telefrag does: gibs raised on the server, and telefrag popups that reach the client.
///         The integration test assembly's systems are registered on both sides; each side only uses its own half.
/// </summary>
public sealed class KsTelefragProbeSystem : EntitySystem
{
    /// <summary>
    ///     Client: every localized popup the server sent, as (anchor entity, message key).
    /// </summary>
    public readonly List<(NetEntity Entity, string Key)> ReceivedPopups = new();

    public override void Initialize()
    {
        // The generator is not imported into the test project, so these are subscribed by hand.
        SubscribeNetworkEvent<KsLocalizedPopupEvent>(OnPopup);
    }

    private void OnPopup(KsLocalizedPopupEvent args)
    {
        ReceivedPopups.Add((args.Entity, args.Message.Id));
    }
}
