using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.Shared._KS14.Fluidmos.Reactions;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Guidebook.Controls;

/// <summary>
///     Shows reactions that run in pipes on a guidebook page, from their prototypes, so the temperatures on the page are
///         always the ones the reactions use: <c>&lt;KsGuideFluidReactionEmbed Reactions="KsDecoking,KsHydrotreating"/&gt;</c>.
/// </summary>
[UsedImplicitly]
public sealed partial class KsGuideFluidReactionEmbed : BoxContainer, IDocumentTag
{
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public KsGuideFluidReactionEmbed()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        var sawmill = _logManager.GetSawmill("guidebook.fluid_reaction");

        if (!args.TryGetValue("Reactions", out var reactions))
        {
            sawmill.Error("Fluid reaction embed tag is missing its Reactions argument");
            return false;
        }

        foreach (var reactionId in reactions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!_prototypeManager.TryIndex<KsFluidReactionPrototype>(reactionId, out var reaction))
            {
                sawmill.Error($"\"{reactionId}\" is not a fluid reaction prototype");
                return false;
            }

            AddChild(new GuideReagentReaction(reaction, _prototypeManager, _entitySystemManager));
        }

        control = this;
        return true;
    }
}
