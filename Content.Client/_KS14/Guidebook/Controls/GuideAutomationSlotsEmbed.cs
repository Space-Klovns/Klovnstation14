// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 GoobBot
// SPDX-FileCopyrightText: 2025 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.Shared._KS14.Factory;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using System.Diagnostics.CodeAnalysis;

namespace Content.Client._KS14.Guidebook.Controls;

/// <summary>
/// Lists all entities with <see cref="AutomationSlotsComponent"/>.
/// </summary>
public sealed partial class GuideAutomationSlotsEmbed : IDocumentTag
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    private readonly AutomationSystem _automation;

    public GuideAutomationSlotsEmbed()
    {
        IoCManager.InjectDependencies(this);

        _automation = _entMan.System<AutomationSystem>();
    }

    bool IDocumentTag.TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        var scroll = new ScrollContainer()
        {
            MinHeight = 200f,
            MaxHeight = 400f
        };
        var box = new BoxContainer()
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true
        };
        foreach (var id in _automation.Automatable)
        {
            box.AddChild(new GuideEntityEmbed(id, false, true));
        }
        scroll.AddChild(box);

        control = scroll;
        return true;
    }
}
