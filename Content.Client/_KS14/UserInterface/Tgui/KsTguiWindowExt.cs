using Content.Client.Guidebook.Components;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     The <c>FancyWindowExt</c> helpers for <see cref="KsTguiWindow"/>, so an interface that set up a fancy window
///         from its entity can set up a tgui one the same way.
/// </summary>
public static class KsTguiWindowExt
{
    /// <summary>
    ///     Titles the window after the entity and points its help button at the entity's guidebook entries.
    /// </summary>
    public static void SetInfoFromEntity(this KsTguiWindow window, IEntityManager entityManager, EntityUid entity)
    {
        window.Title = entityManager.GetComponent<MetaDataComponent>(entity).EntityName;
        window.HelpGuidebookIds = entityManager.GetComponentOrNull<GuideHelpComponent>(entity)?.Guides;
    }
}
